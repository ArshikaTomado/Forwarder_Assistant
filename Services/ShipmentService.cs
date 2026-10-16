// ShipmentService.cs —— 提单业务入口
// 第 1 步：视图模型不再直接碰数据库，统一从这里取数据。
namespace ForwarderAssistant.Services;

using ForwarderAssistant.Data;
using ForwarderAssistant.Models;

/// <summary>提单业务服务：负责建库、首次灌演示数据，并向界面提供提单列表。</summary>
public class ShipmentService
{
    private readonly ShipmentRepository _repo = new();

    /// <summary>取全部提单。首次运行（空库）时先灌一批演示数据。</summary>
    public List<Shipment> GetAll()
    {
        Database.EnsureCreated();
        if (!_repo.HasAnyShipment())
            DemoDataSeeder.Seed(_repo, DateTime.UtcNow);
        return _repo.LoadAll();
    }

    /// <summary>新增一张提单（船期按「船名+航次」存在即复用/更新），返回新提单 id。</summary>
    public long AddShipment(Shipment s) => _repo.Insert(s);

    /// <summary>更新一张提单的抬头字段（船期按「船名+航次」复用/更新）。</summary>
    public void UpdateShipment(Shipment s) => _repo.Update(s);

    /// <summary>历史船名列表（去重），供新建 / 修改提单时挑选复用。</summary>
    public List<string> GetVesselNames() => _repo.LoadVesselNames();

    /// <summary>全部航次的总览（含挂靠港 / 在用提单明细），供「船名 · 航次」窗格预览与清理。</summary>
    public List<VoyageSummary> GetVoyageSummaries() => _repo.LoadVoyageSummaries();

    /// <summary>删除一个航次及其挂靠港。仍有提单在用时拒绝删除，返回其引用数（>0）；成功返回 0。</summary>
    public int DeleteVoyage(long voyageId) => _repo.DeleteVoyage(voyageId);

    /// <summary>重命名航次的「船名 / 航次」。新的「船名 + 航次」与别的航次重名时返回 false；成功返回 true。</summary>
    public bool RenameVoyage(long voyageId, string vesselName, string voyageNo)
        => _repo.RenameVoyage(voyageId, vesselName, voyageNo);

    /// <summary>按「船名 + 航次」查历史提单模板（同港优先），返回其 装/卸港 与对应挂靠港的 开航/到港日，
    /// 供航次联动软回填；找不到返回 null。</summary>
    public (string Pol, string Pod, DateTime? EtdUtc, DateTime? EtaUtc)? FindVoyage(
        string vesselName, string voyageNo, string pol, string pod)
        => _repo.FindVoyage(vesselName, voyageNo, pol, pod);

    /// <summary>给某张提单新增一个节点，返回新的节点 id。</summary>
    public long AddMilestone(long shipmentId, Milestone m) => _repo.InsertMilestone(shipmentId, m);

    /// <summary>更新一个节点。</summary>
    public void UpdateMilestone(Milestone m) => _repo.UpdateMilestone(m);

    /// <summary>删除一个节点。</summary>
    public void DeleteMilestone(long milestoneId) => _repo.DeleteMilestone(milestoneId);

    /// <summary>删除一张提单（连同其全部节点）。</summary>
    public void DeleteShipment(long shipmentId) => _repo.DeleteShipment(shipmentId);

    /// <summary>导入预览：解析 Excel（.xlsx）并对照库中已有提单号，逐条标出是否重复——**不落库**。
    /// 比对范围只比「导入行 vs 库已有」，不查表内；归一化 = Trim + 忽略大小写。</summary>
    public ImportPreview PreviewExcel(string path)
    {
        Database.EnsureCreated();
        var parsed = ExcelImporter.Read(path);

        var existing = new HashSet<string>(
            _repo.LoadExistingBlNos().Select(NormalizeBlNo), StringComparer.OrdinalIgnoreCase);

        var items = parsed.Shipments
            .Select(s => new ImportPreviewItem
            {
                Shipment = s,
                IsDuplicate = existing.Contains(NormalizeBlNo(s.BlNo)),
            })
            .ToList();

        return new ImportPreview { Items = items, Skipped = parsed.Skipped, Warnings = parsed.Warnings };
    }

    /// <summary>把选中的提单逐条落库（一律作新增插入，航次按「船名 + 航次」自动复用/新建）。返回成功条数。</summary>
    public int ImportShipments(IEnumerable<Shipment> shipments)
    {
        Database.EnsureCreated();
        var count = 0;
        foreach (var s in shipments)
        {
            _repo.Insert(s);
            count++;
        }
        return count;
    }

    /// <summary>提单号归一化：去首尾空白（大小写由比较器忽略）。</summary>
    private static string NormalizeBlNo(string blNo) => blNo.Trim();
}
