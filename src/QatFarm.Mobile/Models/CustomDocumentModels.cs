using SQLite;

namespace QatFarm.Mobile.Models;

public enum CustomFieldType
{
    Text = 0,
    Number = 1,
    Money = 2,
    Date = 3,
    Select = 4,
    Checkbox = 5,
    Customer = 6,
    Farm = 7,
    Calculated = 8,
    Notes = 9
}

[Table("CustomDocumentTemplates")]
public sealed class CustomDocumentTemplate : LocalEntity
{
    [Indexed, NotNull] public string Name { get; set; } = string.Empty;
    [Indexed, NotNull] public string Code { get; set; } = string.Empty;
    public string NumberPrefix { get; set; } = "DOC";
    public int NextNumber { get; set; } = 1;
    public string Category { get; set; } = "نموذج";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowLineItems { get; set; } = true;
    public string LineSchemaJson { get; set; } = "[]";
    public string? GrandTotalFormula { get; set; }
    public int Version { get; set; } = 1;
}

[Table("CustomDocumentFields")]
public sealed class CustomDocumentField : LocalEntity
{
    [Indexed] public long TemplateId { get; set; }
    [Indexed, NotNull] public string Key { get; set; } = string.Empty;
    [NotNull] public string Label { get; set; } = string.Empty;
    public CustomFieldType FieldType { get; set; }
    public string Section { get; set; } = "البيانات الأساسية";
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IncludeInPrint { get; set; } = true;
    public bool IsSummary { get; set; }
    public string? DefaultValue { get; set; }
    public string? OptionsJson { get; set; }
    public string? Formula { get; set; }
    public string? Placeholder { get; set; }
}

[Table("CustomDocumentRecords")]
public sealed class CustomDocumentRecord : LocalEntity
{
    [Indexed] public long TemplateId { get; set; }
    [Indexed, NotNull] public string DocumentNumber { get; set; } = string.Empty;
    [Indexed] public DateTime DocumentDate { get; set; } = DateTime.Now;
    public string ValuesJson { get; set; } = "{}";
    public string LinesJson { get; set; } = "[]";
    public decimal GrandTotal { get; set; }
    public string Status { get; set; } = "معتمد";
    public long? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public int TemplateVersion { get; set; } = 1;
    public string? Notes { get; set; }
}

public sealed class CustomLineColumnDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public CustomFieldType FieldType { get; set; } = CustomFieldType.Text;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public string? OptionsJson { get; set; }
    public string? Formula { get; set; }
    public bool IncludeInPrint { get; set; } = true;
}

public sealed class CustomDocumentLineValue
{
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
