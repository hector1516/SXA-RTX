using System.Data;
using Microsoft.Data.SqlClient;

namespace SXA.RTX.Sync.Core.Sync;

internal sealed record ColumnInfo(
    string Name,
    bool IsIdentity,
    bool IsComputed,
    bool IsRowVersion,
    string TypeName,
    int MaxLength,
    byte Precision,
    byte Scale,
    bool IsNullable);

internal sealed class TableSchema
{
    public required string QualifiedName { get; init; }
    public required string KeyColumn { get; init; }
    public required string StatusColumn { get; init; }
    public required string DateColumn { get; init; }
    public required List<ColumnInfo> LocalColumns { get; init; }
    public required List<ColumnInfo> RemoteColumns { get; init; }

    public HashSet<string> RemoteColumnNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool RemoteHasIdentityOnSource { get; init; }

    public bool IsLocalExcluded(ColumnInfo column)
    {
        return column.IsComputed
            || column.IsRowVersion
            || string.Equals(column.Name, StatusColumn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(column.Name, DateColumn, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DatabaseIntrospection
{
    public static async Task<List<ColumnInfo>> GetColumnsAsync(
        SqlConnection connection,
        string qualifiedName,
        CancellationToken cancellationToken)
    {
        const string query = """
            SELECT c.name, c.is_identity, c.is_computed, ISNULL(TYPE_NAME(c.user_type_id), '') AS [type],
                   c.max_length, c.precision, c.scale, c.is_nullable
            FROM sys.columns AS c
            WHERE c.object_id = OBJECT_ID(@qualified)
            ORDER BY c.column_id;
            """;

        var result = new List<ColumnInfo>();
        using var cmd = new SqlCommand(query, connection)
        {
            CommandTimeout = 30
        };
        cmd.Parameters.AddWithValue("@qualified", qualifiedName);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ColumnInfo(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                string.Equals(reader.GetString(3), "timestamp", StringComparison.OrdinalIgnoreCase),
                reader.GetString(3),
                reader.GetInt16(4),
                reader.GetByte(5),
                reader.GetByte(6),
                reader.GetBoolean(7)));
        }

        return result;
    }
}

internal static partial class SqlName
{
    public static string Quote(string tableOrSchema)
    {
        var parts = tableOrSchema.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("Nombre de objeto SQL vacío.", nameof(tableOrSchema));
        }

        if (parts.Length == 1)
        {
            return $"[dbo].[{parts[0]}]";
        }

        return string.Join(".", parts.Select(p => $"[{p}]"));
    }

    public static string Plain(string qualified)
    {
        return qualified
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last()
            .Trim('[', ']');
    }

    public static string Identifier(string name)
    {
        return $"[{name.Trim('[', ']')}]";
    }

    public static string TriggerName(string qualified)
    {
        var plain = Plain(qualified).Replace(' ', '_');
        return $"[dbo].[TRG_SXA_{plain}_SYNC]";
    }

    public static string ConstraintName(string qualified, string suffix)
    {
        var plain = Plain(qualified).Replace(' ', '_');
        return $"PK_SXA_{plain}_{suffix}";
    }

    public static string TypeDefinition(ColumnInfo c)
    {
        var type = c.TypeName.ToLowerInvariant();
        if (type is "nvarchar" or "nchar")
        {
            return c.MaxLength < 0 ? $"{type}(max)" : $"{type}({c.MaxLength / 2})";
        }
        if (type is "varchar" or "char" or "binary" or "varbinary")
        {
            return c.MaxLength < 0 ? $"{type}(max)" : $"{type}({c.MaxLength})";
        }
        return type switch
        {
            "decimal" or "numeric" => $"{type}({c.Precision},{c.Scale})",
            "datetime2" or "datetimeoffset" or "time" => $"{type}({c.Scale})",
            _ => type
        };
    }
}

internal static class SqlTypeMapper
{
    public static SqlDbType Map(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text" or "sysname" => SqlDbType.NVarChar,
            "int" => SqlDbType.Int,
            "bigint" => SqlDbType.BigInt,
            "smallint" => SqlDbType.SmallInt,
            "tinyint" => SqlDbType.TinyInt,
            "bit" => SqlDbType.Bit,
            "datetime" => SqlDbType.DateTime,
            "datetime2" => SqlDbType.DateTime2,
            "smalldatetime" => SqlDbType.SmallDateTime,
            "datetimeoffset" => SqlDbType.DateTimeOffset,
            "date" => SqlDbType.Date,
            "time" => SqlDbType.Time,
            "decimal" or "numeric" => SqlDbType.Decimal,
            "money" => SqlDbType.Money,
            "smallmoney" => SqlDbType.SmallMoney,
            "float" => SqlDbType.Float,
            "real" => SqlDbType.Real,
            "uniqueidentifier" => SqlDbType.UniqueIdentifier,
            "binary" or "varbinary" or "image" or "timestamp" => SqlDbType.VarBinary,
            "xml" => SqlDbType.Xml,
            "sql_variant" => SqlDbType.Variant,
            _ => SqlDbType.NVarChar
        };
    }
}