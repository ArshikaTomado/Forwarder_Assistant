// ExcelImporter.cs —— 读客户给的 Excel（.xlsx），解析成提单草稿
// 只做读取，不碰数据库；落库交给 ShipmentService。
namespace ForwarderAssistant.Services;

using ClosedXML.Excel;
using ForwarderAssistant.Models;

/// <summary>按列头名从 Excel 读出提单草稿。表头必须在第一行可见处，列顺序不限。</summary>
public static class ExcelImporter
{
    /// <summary>解析结果：可用的提单草稿 + 被跳过的行说明（给人看的中文原因）。</summary>
    public sealed class ParseResult
    {
        public List<Shipment> Shipments { get; } = new();
        public List<string> Skipped { get; } = new();
    }

    // 表头文字 → 目标字段。缺列时该项按空处理；「船东提单号 / 船名 / 航次」缺任一项直接报错。
    private const string HBlNo = "船东提单号";
    private const string HVessel = "船名";
    private const string HVoyageNo = "航次";
    private const string HEtd = "开航日期";
    private const string HEta = "到达日期";
    private const string HCustomer = "委托人";
    private const string HPol = "装运港";
    private const string HPod = "卸货港";
    private const string HDest = "目的港";

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

        // 表头文字 → 列号（第一行见到的表头为准，重复表头取首个）
        var col = new Dictionary<string, int>();
        foreach (var cell in ws.Row(headerRow).CellsUsed())
        {
            var name = ReadText(cell);
            if (name.Length > 0 && !col.ContainsKey(name)) col[name] = cell.Address.ColumnNumber;
        }

        var missing = new[] { HBlNo, HVessel, HVoyageNo }.Where(h => !col.ContainsKey(h)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Excel 缺少必需列：{string.Join("、", missing)}。\n请确认表头包含「{HBlNo} / {HVessel} / {HVoyageNo}」。");

        var result = new ParseResult();
        for (var rn = headerRow + 1; rn <= lastRow; rn++)
        {
            var blNo = GetText(ws, col, rn, HBlNo);
            var vessel = GetText(ws, col, rn, HVessel);
            var voyageNo = GetText(ws, col, rn, HVoyageNo);
            var customer = GetText(ws, col, rn, HCustomer);
            var pol = GetText(ws, col, rn, HPol);
            var pod = GetText(ws, col, rn, HPod);

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

            var dest = GetText(ws, col, rn, HDest);
            result.Shipments.Add(new Shipment
            {
                BlNo = blNo,
                Customer = customer,
                Voyage = new Voyage { VesselName = vessel, VoyageNo = voyageNo },
                Pol = pol,
                Pod = pod,
                FinalDestination = dest.Length > 0 ? dest : pod, // 目的港没填就用卸货港（与手动建单一致）
                EtdUtc = ToUtc(GetDate(ws, col, rn, HEtd)),
                EtaUtc = ToUtc(GetDate(ws, col, rn, HEta)),
            });
        }
        return result;
    }

    /// <summary>按表头名取该行文字；表里没有这一列则返回空串。</summary>
    private static string GetText(IXLWorksheet ws, Dictionary<string, int> col, int rn, string header)
        => col.TryGetValue(header, out var c) ? ReadText(ws.Cell(rn, c)) : string.Empty;

    /// <summary>按表头名取该行日期；表里没有这一列则返回 null。</summary>
    private static DateTime? GetDate(IXLWorksheet ws, Dictionary<string, int> col, int rn, string header)
        => col.TryGetValue(header, out var c) ? ReadDate(ws.Cell(rn, c)) : null;

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
