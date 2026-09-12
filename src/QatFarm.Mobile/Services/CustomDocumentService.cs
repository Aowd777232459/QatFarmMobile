using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QatFarm.Mobile.Data;
using QatFarm.Mobile.Models;

namespace QatFarm.Mobile.Services;

public sealed class CustomDocumentService
{
    private readonly MobileDb _db;
    private readonly AppSession _session;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public CustomDocumentService(MobileDb db, AppSession session)
    {
        _db = db;
        _session = session;
    }

    public async Task<List<CustomDocumentTemplate>> GetTemplatesAsync(bool includeInactive = false)
    {
        var db = await _db.GetAsync();
        var rows = await db.Table<CustomDocumentTemplate>().Where(x => !x.IsDeleted).ToListAsync();
        return rows.Where(x => includeInactive || x.IsActive).OrderBy(x => x.Name).ToList();
    }

    public async Task<CustomDocumentTemplate?> GetTemplateAsync(long id)
    {
        var db = await _db.GetAsync();
        return await db.Table<CustomDocumentTemplate>().Where(x => x.Id == id && !x.IsDeleted).FirstOrDefaultAsync();
    }

    public async Task<List<CustomDocumentField>> GetFieldsAsync(long templateId)
    {
        var db = await _db.GetAsync();
        var rows = await db.Table<CustomDocumentField>().Where(x => x.TemplateId == templateId && !x.IsDeleted).ToListAsync();
        return rows.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
    }

    public async Task<CustomDocumentTemplate> SaveTemplateAsync(CustomDocumentTemplate template)
    {
        EnsureAdmin();
        var db = await _db.GetAsync();
        template.Name = Required(template.Name, "اسم النموذج");
        template.Code = NormalizeKey(template.Code, "FORM");
        template.NumberPrefix = NormalizeKey(template.NumberPrefix, template.Code);
        template.UpdatedAt = DateTime.Now;
        if (template.Id == 0)
        {
            template.CreatedAt = DateTime.Now;
            template.SyncKey = Guid.NewGuid().ToString("N");
            template.NextNumber = Math.Max(1, template.NextNumber);
            template.Version = 1;
            await db.InsertAsync(template);
        }
        else
        {
            template.Version = Math.Max(1, template.Version + 1);
            await db.UpdateAsync(template);
        }
        return template;
    }

    public async Task<CustomDocumentField> SaveFieldAsync(CustomDocumentField field)
    {
        EnsureAdmin();
        var db = await _db.GetAsync();
        field.Label = Required(field.Label, "اسم الحقل");
        field.Key = NormalizeKey(field.Key, $"field_{Guid.NewGuid():N}"[..14]);
        field.Section = string.IsNullOrWhiteSpace(field.Section) ? "البيانات الأساسية" : field.Section.Trim();
        field.UpdatedAt = DateTime.Now;
        if (field.Id == 0)
        {
            field.CreatedAt = DateTime.Now;
            field.SyncKey = Guid.NewGuid().ToString("N");
            await db.InsertAsync(field);
        }
        else await db.UpdateAsync(field);
        await TouchTemplateAsync(field.TemplateId);
        return field;
    }

    public async Task DeleteFieldAsync(CustomDocumentField field)
    {
        EnsureAdmin();
        var db = await _db.GetAsync();
        field.IsDeleted = true;
        field.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(field);
        await TouchTemplateAsync(field.TemplateId);
    }

    public async Task DeleteTemplateAsync(CustomDocumentTemplate template)
    {
        EnsureAdmin();
        var db = await _db.GetAsync();
        template.IsDeleted = true;
        template.IsActive = false;
        template.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(template);
    }

    public List<string> ParseOptions(string? jsonOrLines)
    {
        if (string.IsNullOrWhiteSpace(jsonOrLines)) return [];
        try
        {
            if (jsonOrLines.TrimStart().StartsWith('['))
                return JsonSerializer.Deserialize<List<string>>(jsonOrLines, JsonOptions)?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        }
        catch { }
        return jsonOrLines.Split(new[] { '\r', '\n', ',', '،' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
    }

    public string SerializeOptions(IEnumerable<string> values) => JsonSerializer.Serialize(values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(), JsonOptions);

    public List<CustomLineColumnDefinition> GetLineSchema(CustomDocumentTemplate template)
    {
        try { return JsonSerializer.Deserialize<List<CustomLineColumnDefinition>>(template.LineSchemaJson, JsonOptions)?.OrderBy(x => x.SortOrder).ToList() ?? []; }
        catch { return []; }
    }

    public void SetLineSchema(CustomDocumentTemplate template, IEnumerable<CustomLineColumnDefinition> columns) =>
        template.LineSchemaJson = JsonSerializer.Serialize(columns.OrderBy(x => x.SortOrder).ToList(), JsonOptions);

    public async Task<List<CustomDocumentRecord>> GetRecordsAsync(long? templateId = null)
    {
        var db = await _db.GetAsync();
        var rows = await db.Table<CustomDocumentRecord>().Where(x => !x.IsDeleted).ToListAsync();
        if (templateId.HasValue) rows = rows.Where(x => x.TemplateId == templateId.Value).ToList();
        return rows.OrderByDescending(x => x.DocumentDate).ThenByDescending(x => x.Id).ToList();
    }

    public async Task<CustomDocumentRecord> CreateRecordAsync(
        CustomDocumentTemplate template,
        IReadOnlyDictionary<string, string?> inputValues,
        IReadOnlyList<CustomDocumentLineValue> lines,
        string? notes = null)
    {
        var db = await _db.GetAsync();
        var fields = await GetFieldsAsync(template.Id);
        var values = new Dictionary<string, string?>(inputValues, StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (field.FieldType == CustomFieldType.Calculated)
                values[field.Key] = EvaluateFormula(field.Formula, values).ToString("0.####", CultureInfo.InvariantCulture);
            if (field.IsRequired && string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Key)))
                throw new InvalidOperationException($"الحقل «{field.Label}» مطلوب.");
        }

        var lineSchema = GetLineSchema(template);
        var normalizedLines = new List<CustomDocumentLineValue>();
        foreach (var source in lines)
        {
            var row = new CustomDocumentLineValue { Values = new Dictionary<string, string?>(source.Values, StringComparer.OrdinalIgnoreCase) };
            foreach (var column in lineSchema)
            {
                if (column.FieldType == CustomFieldType.Calculated)
                    row.Values[column.Key] = EvaluateFormula(column.Formula, row.Values).ToString("0.####", CultureInfo.InvariantCulture);
                if (column.IsRequired && string.IsNullOrWhiteSpace(row.Values.GetValueOrDefault(column.Key)))
                    throw new InvalidOperationException($"قيمة «{column.Label}» مطلوبة في أحد الصفوف.");
            }
            if (row.Values.Values.Any(x => !string.IsNullOrWhiteSpace(x))) normalizedLines.Add(row);
        }

        var lineTotal = normalizedLines.Sum(row => GuessRowTotal(lineSchema, row.Values));
        values["LINES_TOTAL"] = lineTotal.ToString("0.####", CultureInfo.InvariantCulture);
        var grandTotal = string.IsNullOrWhiteSpace(template.GrandTotalFormula)
            ? lineTotal
            : EvaluateFormula(template.GrandTotalFormula, values);

        await db.RunInTransactionAsync(conn =>
        {
            var fresh = conn.Table<CustomDocumentTemplate>().First(x => x.Id == template.Id);
            var number = $"{fresh.NumberPrefix}-{fresh.NextNumber:00000}";
            fresh.NextNumber++;
            fresh.UpdatedAt = DateTime.Now;
            conn.Update(fresh);

            var record = new CustomDocumentRecord
            {
                TemplateId = template.Id,
                DocumentNumber = number,
                DocumentDate = DateTime.Now,
                ValuesJson = JsonSerializer.Serialize(values, JsonOptions),
                LinesJson = JsonSerializer.Serialize(normalizedLines, JsonOptions),
                GrandTotal = grandTotal,
                Status = "معتمد",
                CreatedByUserId = _session.CurrentUser?.Id,
                CreatedByName = _session.CurrentUser?.FullName,
                TemplateVersion = template.Version,
                Notes = notes,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                SyncKey = Guid.NewGuid().ToString("N")
            };
            conn.Insert(record);
        });

        var created = (await GetRecordsAsync(template.Id)).First();
        return created;
    }

    public Dictionary<string, string?> GetValues(CustomDocumentRecord record)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(record.ValuesJson, JsonOptions) ?? new(StringComparer.OrdinalIgnoreCase); }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public List<CustomDocumentLineValue> GetLines(CustomDocumentRecord record)
    {
        try { return JsonSerializer.Deserialize<List<CustomDocumentLineValue>>(record.LinesJson, JsonOptions) ?? []; }
        catch { return []; }
    }

    public decimal EvaluateFormula(string? formula, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrWhiteSpace(formula)) return 0m;
        var expression = Regex.Replace(formula, @"\[([A-Za-z0-9_]+)\]", match =>
        {
            var key = match.Groups[1].Value;
            var raw = values.TryGetValue(key, out var value) ? value : "0";
            return ParseNumber(raw).ToString(CultureInfo.InvariantCulture);
        });
        return ArithmeticExpression.Evaluate(expression);
    }

    private static decimal GuessRowTotal(IReadOnlyList<CustomLineColumnDefinition> schema, IReadOnlyDictionary<string, string?> values)
    {
        var calculated = schema.LastOrDefault(x => x.FieldType == CustomFieldType.Calculated);
        if (calculated is not null && values.TryGetValue(calculated.Key, out var result)) return ParseNumber(result);
        foreach (var key in new[] { "total", "line_total", "amount", "الإجمالي" })
            if (values.TryGetValue(key, out var value)) return ParseNumber(value);
        return 0m;
    }

    private async Task TouchTemplateAsync(long templateId)
    {
        var db = await _db.GetAsync();
        var template = await db.Table<CustomDocumentTemplate>().Where(x => x.Id == templateId).FirstOrDefaultAsync();
        if (template is null) return;
        template.Version = Math.Max(1, template.Version + 1);
        template.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(template);
    }

    private void EnsureAdmin()
    {
        if (!_session.IsAdmin) throw new InvalidOperationException("مصمم النماذج متاح لمدير النظام فقط.");
    }

    private static string Required(string? value, string label)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length == 0) throw new InvalidOperationException($"{label} مطلوب.");
        return result;
    }

    public static string NormalizeKey(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var chars = source.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    public static decimal ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var normalized = value.Trim().Replace("٬", "").Replace(",", "").Replace("٫", ".");
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0m;
    }

    private static class ArithmeticExpression
    {
        public static decimal Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return 0m;
            var tokens = Tokenize(expression);
            var output = new Stack<decimal>();
            var ops = new Stack<char>();
            foreach (var token in tokens)
            {
                if (token.Number.HasValue) output.Push(token.Number.Value);
                else if (token.Op == '(') ops.Push('(');
                else if (token.Op == ')')
                {
                    while (ops.Count > 0 && ops.Peek() != '(') Apply(output, ops.Pop());
                    if (ops.Count == 0 || ops.Pop() != '(') throw new InvalidOperationException("صيغة العملية الحسابية غير صحيحة.");
                }
                else
                {
                    while (ops.Count > 0 && ops.Peek() != '(' && Precedence(ops.Peek()) >= Precedence(token.Op)) Apply(output, ops.Pop());
                    ops.Push(token.Op);
                }
            }
            while (ops.Count > 0) Apply(output, ops.Pop());
            if (output.Count != 1) throw new InvalidOperationException("صيغة العملية الحسابية غير صحيحة.");
            return decimal.Round(output.Pop(), 4, MidpointRounding.AwayFromZero);
        }

        private static List<Token> Tokenize(string text)
        {
            var result = new List<Token>();
            var i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }
                var c = text[i];
                if (c is '+' or '-' or '*' or '/' or '(' or ')')
                {
                    if (c == '-' && (result.Count == 0 || (!result[^1].Number.HasValue && result[^1].Op != ')')))
                    {
                        var start = i++;
                        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                        if (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                        {
                            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                            result.Add(new Token(decimal.Parse(text[start..i].Replace(" ", ""), CultureInfo.InvariantCulture), '\0'));
                            continue;
                        }
                        result.Add(new Token(0m, '\0'));
                    }
                    result.Add(new Token(null, c)); i++; continue;
                }
                if (char.IsDigit(c) || c == '.')
                {
                    var start = i++;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                    if (!decimal.TryParse(text[start..i], NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                        throw new InvalidOperationException("يوجد رقم غير صحيح في العملية الحسابية.");
                    result.Add(new Token(n, '\0')); continue;
                }
                throw new InvalidOperationException($"رمز غير مسموح في العملية الحسابية: {c}");
            }
            return result;
        }

        private static int Precedence(char op) => op is '*' or '/' ? 2 : 1;
        private static void Apply(Stack<decimal> values, char op)
        {
            if (values.Count < 2) throw new InvalidOperationException("صيغة العملية الحسابية غير صحيحة.");
            var b = values.Pop(); var a = values.Pop();
            values.Push(op switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => b == 0 ? 0 : a / b,
                _ => throw new InvalidOperationException("عملية حسابية غير مدعومة.")
            });
        }
        private readonly record struct Token(decimal? Number, char Op);
    }
}
