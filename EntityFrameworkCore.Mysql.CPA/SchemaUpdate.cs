using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using EntityFrameworkCore.Mysql.CPA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;


namespace EntityFrameworkCore.Mysql.CPA
{
    internal sealed class SchemaUpdate
    {
        internal async Task StartAsync(DbContext context)
        {
            try
            {
                // 检查数据库是否存在，不存在则创建
                bool isNewDatabase = !await context.Database.CanConnectAsync();
                await EnsureDatabaseExists(context);
                
                // 如果是新创建的数据库，就不需要再进行后续操作
                if (!isNewDatabase)
                {
                    // 在应用启动时检查并更新数据库结构
                    await UpdateDatabaseSchema(context);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to initialize database: {ex.Message}", ex);
            }
        }

        private async Task EnsureDatabaseExists(DbContext context)
        {
            try
            {
                // 使用 EF Core 的 EnsureCreated 方法创建数据库
                await context.Database.EnsureCreatedAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create database: {ex.Message}", ex);
            }
        }

        internal Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private bool IsCompatibleTypeChange(Type currentType, Type newType)
        {
            // 定义安全的类型转换
            var safeConversions = new Dictionary<Type, HashSet<Type>> {
            // 数值类型的安全转换
            { typeof(int), new HashSet<Type> { typeof(long), typeof(decimal) } },
            { typeof(float), new HashSet<Type> { typeof(double) } },
            { typeof(short), new HashSet<Type> { typeof(int), typeof(long) } },

            // 字符串类型的转换
            { typeof(string), new HashSet<Type> { typeof(string) } }, // nvarchar 长度变化在别处处理

            // 日期时间类型
            { typeof(DateTime), new HashSet<Type> { typeof(DateTimeOffset) } }
        };

            return safeConversions.TryGetValue(currentType, out var safeTypes) &&
                   safeTypes.Contains(newType);
        }

        private async Task UpdateDatabaseSchema(DbContext context)
        {
            try
            {
                // 获取当前数据库结构
                var currentSchema = await GetCurrentSchema(context);

                // 获取实体类定义的结构
                var entitySchema = GetEntitySchema(context);

                // 记录新创建的表名
                var newlyCreatedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 检查是否需要创建新表
                foreach (var entityTable in entitySchema)
                {
                    var tableExists = currentSchema.Any(t => 
                        string.Equals(t.Name, entityTable.Name, StringComparison.OrdinalIgnoreCase));

                    if (!tableExists)
                    {
                        // 创建新表
                        await CreateTable(context, entityTable);
                        newlyCreatedTables.Add(entityTable.Name);
                        continue;
                    }
                }

                // 只对比未新建的表的字段差异
                var tablesToCompare = entitySchema
                    .Where(t => !newlyCreatedTables.Contains(t.Name))
                    .ToList();

                // 比较并生成更新
                var differences = CompareSchemaDifferences(currentSchema, tablesToCompare);

                // 应用更改
                if (differences.Any())
                {
                    await ApplySchemaChanges(context, differences);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to update database schema: {ex.Message}", ex);
            }
        }

        private async Task CreateTable(DbContext context, TableInfo tableInfo)
        {
            var columns = new List<string>();
            var primaryKeys = new List<string>();

            foreach (var column in tableInfo.Columns)
            {
                var sqlType = GetMySqlType(column.Type, column);
                var nullableString = column.IsNullable ? "NULL" : "NOT NULL";
                var autoIncrementString = GetAutoIncrementString(column);
                
                columns.Add($"`{column.Name}` {sqlType} {autoIncrementString} {nullableString}");
                
                if (column.IsPrimaryKey)
                {
                    primaryKeys.Add(column.Name);
                }
            }

            var sql = $@"CREATE TABLE `{tableInfo.Name}` (
                {string.Join(",\n", columns)}
                {(primaryKeys.Any() ? $",\nPRIMARY KEY ({string.Join(", ", primaryKeys.Select(pk => $"`{pk}`"))})" : "")}
            )";

            await context.Database.ExecuteSqlRawAsync(sql);
        }

        private bool HasColumnChanged(ColumnInfo current, ColumnInfo entity)
        {
            if (!string.Equals(current.Name, entity.Name, StringComparison.OrdinalIgnoreCase))
                return true;

            if (current.Type != entity.Type && !IsCompatibleTypeChange(current.Type, entity.Type))
                return true;

            if (current.IsNullable != entity.IsNullable)
                return true;

            if (current.IsPrimaryKey != entity.IsPrimaryKey)
                return true;

            if (current.IsAutoIncrement != entity.IsAutoIncrement)
                return true;

            if (current.MaxLength != entity.MaxLength)
                return true;

            if (current.IsUnique != entity.IsUnique)
                return true;

            if (current.Precision != entity.Precision)
                return true;

            if (current.Scale != entity.Scale)
                return true;

            if (!string.Equals(current.Collation, entity.Collation, StringComparison.OrdinalIgnoreCase))
                return true;

            // 对于默认值的比较，需要考虑数据库特定的格式
            if (!CompareDefaultValues(current.DefaultValue, entity.DefaultValue))
                return true;

            return false;
        }

        private bool CompareDefaultValues(string currentDefault, string entityDefault)
        {
            if (currentDefault == entityDefault)
                return true;

            // 处理特殊的默认值比较
            if (currentDefault == null || entityDefault == null)
                return currentDefault == entityDefault;

            // 清理数据库特定的默认值格式
            currentDefault = CleanDefaultValue(currentDefault);
            entityDefault = CleanDefaultValue(entityDefault);

            return string.Equals(currentDefault, entityDefault, StringComparison.OrdinalIgnoreCase);
        }

        private string CleanDefaultValue(string defaultValue)
        {
            if (string.IsNullOrEmpty(defaultValue))
                return defaultValue;

            // 移除引号
            defaultValue = defaultValue.Trim('\'', '"');

            // 处理特殊的默认值
            switch (defaultValue.ToUpper())
            {
                case "CURRENT_TIMESTAMP":
                case "CURRENT_DATE":
                case "NOW()":
                    return "CURRENT_TIMESTAMP";
                default:
                    return defaultValue;
            }
        }

        private IList<SchemaDifference> CompareSchemaDifferences(
            IList<TableInfo> currentSchema,
            IList<TableInfo> entitySchema)
        {
            var differences = new List<SchemaDifference>();

            foreach (var entityTable in entitySchema)
            {
                // 不区分大小写比较表名
                var currentTable = currentSchema.FirstOrDefault(t =>
                    string.Equals(t.Name, entityTable.Name, StringComparison.OrdinalIgnoreCase));

                if (currentTable == null)
                {
                    // 这是一个全新的表
                    differences.AddRange(entityTable.Columns.Select(c => new SchemaDifference
                    {
                        Type = DifferenceType.AddColumn,
                        TableName = entityTable.Name,
                        NewColumn = c
                    }));
                    continue;
                }

                // 比较列的差异
                foreach (var entityColumn in entityTable.Columns)
                {
                    Console.WriteLine($"Checking column: {entityColumn.Name} in table {entityTable.Name}");

                    // 不区分大小写比较列名
                    var currentColumn = currentTable.Columns.FirstOrDefault(c =>
                        string.Equals(c.Name, entityColumn.Name, StringComparison.OrdinalIgnoreCase));

                    if (currentColumn == null)
                    {
                        // 这是一个新列
                        Console.WriteLine($"New column found: {entityColumn.Name}");
                        differences.Add(new SchemaDifference
                        {
                            Type = DifferenceType.AddColumn,
                            TableName = entityTable.Name,
                            NewColumn = entityColumn
                        });
                    }
                    else if (HasColumnChanged(currentColumn, entityColumn))
                    {
                        // 这是一个需要修改的列
                        Console.WriteLine($"Column changed: {entityColumn.Name}");
                        differences.Add(new SchemaDifference
                        {
                            Type = DifferenceType.ModifyColumn,
                            TableName = entityTable.Name,
                            OldColumn = currentColumn,
                            NewColumn = entityColumn
                        });
                    }
                }

                // 检查需要删除的列
                foreach (var currentColumn in currentTable.Columns)
                {
                    if (!entityTable.Columns.Any(c =>
                        string.Equals(c.Name, currentColumn.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        differences.Add(new SchemaDifference
                        {
                            Type = DifferenceType.RemoveColumn,
                            TableName = entityTable.Name,
                            OldColumn = currentColumn
                        });
                    }
                }
            }

            return differences;
        }
        private async Task<IList<TableInfo>> GetCurrentSchema(DbContext context)
        {
            var tables = new List<TableInfo>();
            var sql = @"
            SELECT 
                TABLE_NAME,
                COLUMN_NAME,
                DATA_TYPE,
                IS_NULLABLE,
                CHARACTER_MAXIMUM_LENGTH,
                NUMERIC_PRECISION,
                NUMERIC_SCALE,
                COLUMN_TYPE,
                COLUMN_DEFAULT,
                COLLATION_NAME,
                COLUMN_KEY,
                EXTRA
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = DATABASE()";

            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

           


            await context.Database.OpenConnectionAsync();



            using var result = await command.ExecuteReaderAsync();

            while (await result.ReadAsync())
            {
                int tableNameOrd = result.GetOrdinal("TABLE_NAME");
                int columnNameOrd = result.GetOrdinal("COLUMN_NAME");
                int dataTypeOrd = result.GetOrdinal("DATA_TYPE");
                int isNullableOrd = result.GetOrdinal("IS_NULLABLE");
                int maxLengthOrd = result.GetOrdinal("CHARACTER_MAXIMUM_LENGTH");
                int precisionOrd = result.GetOrdinal("NUMERIC_PRECISION");
                int scaleOrd = result.GetOrdinal("NUMERIC_SCALE");
                int columnTypeOrd = result.GetOrdinal("COLUMN_TYPE");
                int defaultValueOrd = result.GetOrdinal("COLUMN_DEFAULT");
                int collationOrd = result.GetOrdinal("COLLATION_NAME");
                int columnKeyOrd = result.GetOrdinal("COLUMN_KEY");
                int extraOrd = result.GetOrdinal("EXTRA");

                var tableName = result.IsDBNull(tableNameOrd) ? null : result.GetString(tableNameOrd);
                var columnName = result.IsDBNull(columnNameOrd) ? null : result.GetString(columnNameOrd);
                var dataType = result.IsDBNull(dataTypeOrd) ? null : result.GetString(dataTypeOrd);
                var isNullable = result.IsDBNull(isNullableOrd) ? true : result.GetString(isNullableOrd) == "YES";
                var maxLength = result.IsDBNull(maxLengthOrd) ? null : (int?)result.GetInt32(maxLengthOrd);
                var precision = result.IsDBNull(precisionOrd) ? null : (int?)result.GetInt32(precisionOrd);
                var scale = result.IsDBNull(scaleOrd) ? null : (int?)result.GetInt32(scaleOrd);
                var columnType = result.IsDBNull(columnTypeOrd) ? null : result.GetString(columnTypeOrd);
                var defaultValue = result.IsDBNull(defaultValueOrd) ? null : result.GetString(defaultValueOrd);
                var collation = result.IsDBNull(collationOrd) ? null : result.GetString(collationOrd);
                var columnKey = result.IsDBNull(columnKeyOrd) ? null : result.GetString(columnKeyOrd);
                var extra = result.IsDBNull(extraOrd) ? null : result.GetString(extraOrd);

                if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(columnName))
                    continue;

                var tableInfo = tables.FirstOrDefault(t => t.Name == tableName);
                if (tableInfo == null)
                {
                    tableInfo = new TableInfo { Name = tableName };
                    tables.Add(tableInfo);
                }

                tableInfo.Columns.Add(new ColumnInfo
                {
                    Name = columnName,
                    Type = !string.IsNullOrEmpty(dataType) ? MapMySqlTypeToClrType(dataType) : typeof(string),
                    IsNullable = isNullable,
                    MaxLength = maxLength,
                    ColumnType = columnType,
                    DefaultValue = defaultValue,
                    Collation = collation,
                    IsPrimaryKey = columnKey == "PRI",
                    IsUnique = columnKey == "UNI",
                    IsAutoIncrement = extra?.Contains("auto_increment") ?? false,
                    Precision = precision,
                    Scale = scale
                });
            }

            return tables;
        }


        private Type MapMySqlTypeToClrType(string mySqlType)
        {
            switch (mySqlType.ToLower())
            {
                case "int":
                case "tinyint":
                case "smallint":
                case "mediumint":
                    return typeof(int);
                case "bigint":
                    return typeof(long);
                case "varchar":
                case "text":
                case "char":
                case "nvarchar":
                case "nchar":
                    return typeof(string);
                case "datetime":
                case "timestamp":
                    return typeof(DateTime);
                case "bit":
                case "bool":
                case "boolean":
                    return typeof(bool);
                case "decimal":
                case "numeric":
                    return typeof(decimal);
                case "float":
                    return typeof(float);
                case "double":
                    return typeof(double);
                default:
                    throw new NotSupportedException($"MySQL type {mySqlType} is not supported");
            }
        }

        private IList<TableInfo> GetEntitySchema(DbContext context)
        {
            var tables = new List<TableInfo>();
            var model = context.GetService<IDesignTimeModel>().Model;

            foreach (var entityType in model.GetEntityTypes())
            {
                var tableName = entityType.GetTableName() ?? entityType.ClrType.Name;
                var tableInfo = new TableInfo { Name = tableName };

                foreach (var property in entityType.GetProperties())
                {
                    // 获取列的所有特性
                    var columnAttr = property.PropertyInfo?.GetCustomAttribute<ColumnAttribute>();
                    var maxLengthAttr = property.PropertyInfo?.GetCustomAttribute<MaxLengthAttribute>();
                    var requiredAttr = property.PropertyInfo?.GetCustomAttribute<RequiredAttribute>();
                    var stringLengthAttr = property.PropertyInfo?.GetCustomAttribute<StringLengthAttribute>();
                    var precisionAttr = property.PropertyInfo?.GetCustomAttribute<PrecisionAttribute>();
                    var databaseGeneratedAttr = property.PropertyInfo?.GetCustomAttribute<DatabaseGeneratedAttribute>();

                    var columnInfo = new ColumnInfo
                    {
                        Name = property.GetColumnName() ?? property.Name,
                        Type = property.ClrType,
                        IsNullable = requiredAttr == null && property.IsNullable,
                        IsPrimaryKey = property.IsPrimaryKey(),
                        IsAutoIncrement = property.ValueGenerated == ValueGenerated.OnAdd ||
                                         (databaseGeneratedAttr?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity),

                        // 处理特性信息
                        MaxLength = maxLengthAttr?.Length ?? stringLengthAttr?.MaximumLength,
                        ColumnType = columnAttr?.TypeName,
                        IsUnique = property.IsIndex() && property.GetContainingIndexes().Any(i => i.IsUnique),
                        Precision = precisionAttr?.Precision,
                        Scale = precisionAttr?.Scale,
                        DatabaseGenerated = databaseGeneratedAttr?.DatabaseGeneratedOption
                    };

                    // 从设计时模型获取更多信息
                    var configuration = entityType.FindProperty(property.Name);
                    if (configuration != null)
                    {
                        columnInfo.DefaultValue = configuration.GetDefaultValue()?.ToString();
                        // 使用正确的方法获取 Collation
                        var relationalProperty = configuration.FindAnnotation("Relational:Collation");
                        columnInfo.Collation = relationalProperty?.Value?.ToString();
                    }

                    tableInfo.Columns.Add(columnInfo);
                }

                tables.Add(tableInfo);
            }

            return tables;
        }


        private async Task AddColumn(DbContext context, SchemaDifference diff)
        {
            var sqlType = GetMySqlType(diff.NewColumn.Type, diff.NewColumn);
            var nullableString = diff.NewColumn.IsNullable ? "NULL" : "NOT NULL";
            var autoIncrementString = GetAutoIncrementString(diff.NewColumn);

            var sql = $"ALTER TABLE `{diff.TableName}` ADD `{diff.NewColumn.Name}` {sqlType} {autoIncrementString} {nullableString}";
            await context.Database.ExecuteSqlRawAsync(sql);
        }

        private async Task ModifyColumn(DbContext context, SchemaDifference diff)
        {
            var sqlType = GetMySqlType(diff.NewColumn.Type, diff.NewColumn);
            var nullableString = diff.NewColumn.IsNullable ? "NULL" : "NOT NULL";
            var autoIncrementString = GetAutoIncrementString(diff.NewColumn);

            var sql = $"ALTER TABLE `{diff.TableName}` MODIFY COLUMN `{diff.NewColumn.Name}` {sqlType} {autoIncrementString} {nullableString}";
            await context.Database.ExecuteSqlRawAsync(sql);
        }

        private string GetAutoIncrementString(ColumnInfo columnInfo)
        {
            if (columnInfo.DatabaseGenerated == DatabaseGeneratedOption.Identity ||
                (columnInfo.IsAutoIncrement && columnInfo.IsPrimaryKey))
            {
                return "AUTO_INCREMENT";
            }
            else if (columnInfo.DatabaseGenerated == DatabaseGeneratedOption.Computed)
            {
                // 处理计算列
                return "GENERATED ALWAYS AS (/* 计算表达式 */)";
            }
            return string.Empty;
        }

        private string GetMySqlType(Type type, ColumnInfo columnInfo)
        {
            if (type == typeof(string))
            {
                // 使用 MaxLength 特性指定的长度
                var length = columnInfo.MaxLength ?? 255; // 如果没有指定，默认使用 255
                return $"varchar({length})";
            }
            if (type == typeof(int))
                return "int";
            if (type == typeof(long))
                return "bigint";
            if (type == typeof(DateTime))
                return "datetime";
            if (type == typeof(bool))
                return "tinyint(1)";
            if (type == typeof(decimal))
            {
                // 使用 Precision 和 Scale
                var precision = columnInfo.Precision ?? 18;
                var scale = columnInfo.Scale ?? 2;
                return $"decimal({precision},{scale})";
            }
            if (type == typeof(float))
                return "float";
            if (type == typeof(double))
                return "double";

            throw new NotSupportedException($"Type {type.Name} is not supported");
        }

        private async Task RemoveColumn(DbContext context, SchemaDifference diff)
        {
            var sql = $"ALTER TABLE `{diff.TableName}` DROP COLUMN `{diff.OldColumn.Name}`";
            await context.Database.ExecuteSqlRawAsync(sql);
        }

        private async Task ApplySchemaChanges(DbContext context, IList<SchemaDifference> differences)
        {
            foreach (var diff in differences)
            {
                switch (diff.Type)
                {
                    case DifferenceType.AddColumn:
                        await AddColumn(context, diff);
                        break;
                    case DifferenceType.RemoveColumn:
                        await RemoveColumn(context, diff);
                        break;
                    case DifferenceType.ModifyColumn:
                        await ModifyColumn(context, diff);
                        break;
                }
            }
        }
    }
}
