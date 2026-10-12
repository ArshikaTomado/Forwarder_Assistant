// VoyageSummary.cs —— 航次总览（「船名 · 航次」管理窗格用；只读展示，不是持久化实体）
namespace ForwarderAssistant.Models;

/// <summary>一个航次的总览：身份 + 挂靠港数 / 在用提单数 / 最近开航日，附带明细（挂靠港、在用提单号）。</summary>
public class VoyageSummary
{
    /// <summary>航次数据库主键</summary>
    public long Id { get; set; }

    /// <summary>船名</summary>
    public string VesselName { get; set; } = "";

    /// <summary>航次</summary>
    public string VoyageNo { get; set; } = "";

    /// <summary>挂靠港条数</summary>
    public int PortCount { get; set; }

    /// <summary>还在用这个航次的提单数（>0 时不允许删除该航次）</summary>
    public int ShipmentCount { get; set; }

    /// <summary>最近一次开航日（UTC），取自各挂靠港开航日里最晚的一个；无则为 null</summary>
    public DateTime? LastDepartureUtc { get; set; }

    /// <summary>挂靠港明细（只读预览）</summary>
    public List<VoyagePort> Ports { get; set; } = new();

    /// <summary>在用提单号明细（只读预览）</summary>
    public List<string> ShipmentBlNos { get; set; } = new();

    /// <summary>最近开航日文本（本地日期，仅界面显示用）；无则为「—」</summary>
    public string LastDepartureText
        => LastDepartureUtc is { } d ? d.ToLocalTime().ToString("yyyy-MM-dd") : "—";
}
