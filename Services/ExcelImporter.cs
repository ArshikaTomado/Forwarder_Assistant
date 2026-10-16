// ExcelImporter.cs —— 读客户给的 Excel（.xlsx），解析成提单草稿
// 只做读取，不碰数据库；落库交给 ShipmentService。
namespace ForwarderAssistant.Services;

using ClosedXML.Excel;
using ForwarderAssistant.Models;

/// <summary>按列头名从 Excel 读出提单草稿。表头必须在第一行可见处，列顺序不限；
/// 每个字段接受一组别名（如「船东提单号 / 主提单号 / 提单号」），命中任一即可。</summary>
public static class ExcelImporter
{
    /// <summary>解析结果：可用的提单草稿 + 被跳过的行说明 + 表头冲突等提示（均为给人看的中文）。</summary>
    public sealed class ParseResult
    {
        public List<Shipment> Shipments { get; } = new();
        public List<string> Skipped { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    /// <summary>目标字段。顺序即「同一列被多个字段命中」时先到先得的优先级。</summary>
    private enum Field { BlNo, Vessel, VoyageNo, Etd, Eta, Customer, Pol, Pod, Dest }

    private static readonly Field[] AllFields =
    {
        Field.BlNo, Field.Vessel, Field.VoyageNo, Field.Etd, Field.Eta,
        Field.Customer, Field.Pol, Field.Pod, Field.Dest,
    };

    // 每个字段可接受的表头别名（不区分大小写）。缺列时该字段按空处理；
    // 「提单号 / 船名 / 航次」缺任一项直接报错。不做「船名 / 航次」合成列的自动拆分。
    private static readonly Dictionary<Field, string[]> Aliases = new()
    {
        [Field.BlNo] = new[] { "船东提单号", "主提单号", "提单号" },
        [Field.Vessel] = new[] { "船名" },
        [Field.VoyageNo] = new[] { "航次" },
        [Field.Etd] = new[] { "开航日期", "开船日期", "开航期", "ETD" },
        [Field.Eta] = new[] { "到达日期", "到港日期", "到港期", "ETA" },
        [Field.Customer] = new[] { "委托人", "客户", "客户名称" },
        [Field.Pol] = new[] { "装运港", "起运港", "装货港", "POL" },
        [Field.Pod] = new[] { "卸货港", "POD" },
        [Field.Dest] = new[] { "目的港", "最终目的地", "最终交货地" },
    };

    /// <summary>字段的中文显示名（报错与提示用）。</summary>
    private static readonly Dictionary<Field, string> DisplayNames = new()
    {
        [Field.BlNo] = "提单号",
        [Field.Vessel] = "船名",
        [Field.VoyageNo] = "航次",
        [Field.Etd] = "开航日期",
        [Field.Eta] = "到达日期",
        [Field.Customer] = "委托人",
        [Field.Pol] = "装运港",
        [Field.Pod] = "卸货港",
        [Field.Dest] = "目的港",
    };

    /// <summary>读第一个工作表，返回提单草稿与被跳过的行说明。</summary>
    /// <exception cref="InvalidOperationException">文件不可读 / 空表 / 缺关键列时抛出，消息可直接展示给用户。</exception>
    public static ParseResult Read(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.FirstOrDefault()
                 ?? throw new InvalidOperationException("Excel 里没有任何工作表。");

        var used = ws.RangeUsed()
                   ?? throw new InvalidOperationException("Excel 是空的，没有读到任何内容。");
        var headerRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();

        var result = new ParseResult();

        // 表头文字 → 字段的列映射（按别名命中；同列冲突先到先得，落败方进 Warnings）
        var colOf = MapColumns(ws, headerRow, result.Warnings);

        var missing = new[] { Field.BlNo, Field.Vessel, Field.VoyageNo }
            .Where(f => !colOf.ContainsKey(f))
            .Select(f => $"{DisplayNames[f]}（可接受的表头：{string.Join(" / ", Aliases[f])}）")
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException("Excel 缺少必需列：\n" + string.Join("\n", missing));

        for (var rn = headerRow + 1; rn <= lastRow; rn++)
        {
            var blNo = GetText(ws, colOf, rn, Field.BlNo);
            var vessel = GetText(ws, colOf, rn, Field.Vessel);
            var voyageNo = GetText(ws, colOf, rn, Field.VoyageNo);
            var customer = GetText(ws, colOf, rn, Field.Customer);
            var pol = GetText(ws, colOf, rn, Field.Pol);
            var pod = GetText(ws, colOf, rn, Field.Pod);

            // 整行空白（关心的列全空）→ 静默跳过
            if (blNo.Length == 0 && vessel.Length == 0 && voyageNo.Length == 0
                && customer.Length == 0 && pol.Length == 0 && pod.Length == 0)
                continue;

            if (blNo.Length == 0) { result.Skipped.Add($"第 {rn} 行：提单号为空"); continue; }
            if (vessel.Length == 0 || voyageNo.Length == 0)
            {
                result.Skipped.Add($"第 {rn} 行（提单 {blNo}）：船名 / 航次为空");
                continue;
            }

            var dest = GetText(ws, colOf, rn, Field.Dest);
            var etaDate = GetDate(ws, colOf, rn, Field.Eta);

            var shipment = new Shipment
            {
                BlNo = blNo,
                Customer = customer,
                Voyage = new Voyage { VesselName = vessel, VoyageNo = voyageNo },
                Pol = pol,
                Pod = pod,
                FinalDestination = dest.Length > 0 ? dest : pod, // 目的港没填就用卸货港（与手动建单一致）
                EtdUtc = ToUtc(GetDate(ws, colOf, rn, Field.Etd)),
                EtaUtc = ToUtc(etaDate),
            };

            AddAutoReminder(shipment, etaDate);
            result.Shipments.Add(shipment);
        }
        return result;
    }

    /// <summary>导入即自动补的提醒：有 ETA 的提单，加一个「放单付费提醒」仅日期节点，定在 ETA 前 7 天。
    /// 规则写死、不可配（见 项目计划）；落库时随提单一起写入（ShipmentRepository.Insert 会遍历 Milestones）。</summary>
    private static void AddAutoReminder(Shipment s, DateTime? etaDate)
    {
        if (etaDate is null) return;

        s.Milestones.Add(new Milestone
        {
            Kind = "custom",
            Label = "放单付费提醒",
            DueDate = DateOnly.FromDateTime(etaDate.Value.Date.AddDays(-7)),
        });
    }

    /// <summary>把表头行映射成「字段 → 列号」：按字段定义顺序先到先得；一列已被先到的字段占用，
    /// 后到的字段不能再取它。若一列头同时还能对上后面的某个字段，则给落败方记一条提示。</summary>
    private static Dictionary<Field, int> MapColumns(IXLWorksheet ws, int headerRow, List<string> warnings)
    {
        var headers = ws.Row(headerRow).CellsUsed()
            .Select(c => (Col: c.Address.ColumnNumber, Name: ReadText(c)))
            .Where(h => h.Name.Length > 0)
            .ToList();

        var colOf = new Dictionary<Field, int>();
        for (var i = 0; i < AllFields.Length; i++)
        {
            var field = AllFields[i];
            var used = new HashSet<int>(colOf.Values);
            var hit = headers.FirstOrDefault(h =>
                !used.Contains(h.Col) && Aliases[field].Any(a => HeaderEquals(h.Name, a)));
            if (hit.Name is null) continue;   // 该字段没有可用列

            colOf[field] = hit.Col;

            // 这一列头同时也能对上后面的某个字段 → 落败方记一条提示（先到先得）
            foreach (var other in AllFields.Skip(i + 1))
                if (Aliases[other].Any(a => HeaderEquals(hit.Name, a)))
                    warnings.Add($"表头「{hit.Name}」既像「{DisplayNames[field]}」又像「{DisplayNames[other]}」，"
                                 + $"已按先到先得取用「{DisplayNames[field]}」。");
        }
        return colOf;
    }

    /// <summary>表头文字比对：不区分大小写。</summary>
    private static bool HeaderEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>按字段取该行文字；表里没有这个字段的列则返回空串。</summary>
    private static string GetText(IXLWorksheet ws, Dictionary<Field, int> colOf, int rn, Field field)
        => colOf.TryGetValue(field, out var c) ? ReadText(ws.Cell(rn, c)) : string.Empty;

    /// <summary>按字段取该行日期；表里没有这个字段的列则返回 null。</summary>
    private static DateTime? GetDate(IXLWorksheet ws, Dictionary<Field, int> colOf, int rn, Field field)
        => colOf.TryGetValue(field, out var c) ? ReadDate(ws.Cell(rn, c)) : null;

    /// <summary>单元格取文字（去掉首尾空白；数字、文本都按显示值取）。</summary>
    private static string ReadText(IXLCell cell)
        => cell.IsEmpty() ? string.Empty : cell.GetFormattedString().Trim();

    /// <summary>单元格取日期（兼容真日期、文本日期、以及没设格式的日期序列号）。</summary>
    private static DateTime? ReadDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime)
        {
            var dt = cell.GetDateTime();
            return dt.Year <= 1900 ? null : dt.Date; // 全 0 / 占位值当空
        }
        if (cell.DataType == XLDataType.Number)
        {
            var serial = cell.GetDouble();
            return serial <= 0 ? null : DateTime.FromOADate(serial).Date; // 没套日期格式的序列号
        }
        var text = cell.GetFormattedString().Trim();
        return DateTime.TryParse(text, out var parsed) ? parsed.Date : null;
    }

    // 仅日期的「本地日历日」→ 入库 UTC（与手动建单 dialog 的 ToUtc 完全一致）
    private static DateTime? ToUtc(DateTime? date)
        => date is null
            ? null
            : DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Local).ToUniversalTime();
}
