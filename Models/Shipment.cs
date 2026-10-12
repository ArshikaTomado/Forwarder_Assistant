// Shipment.cs —— 提单实体（第 0 步仅用于展示假数据）
namespace ForwarderAssistant.Models;

/// <summary>一张提单。提单号 BlNo 是业务唯一键。</summary>
public class Shipment
{
    /// <summary>数据库主键（新增节点时用来定位归属提单）</summary>
    public long Id { get; set; }

    /// <summary>提单号，业务唯一键</summary>
    public string BlNo { get; set; } = "";

    /// <summary>客户</summary>
    public string Customer { get; set; } = "";

    /// <summary>所属航次（船名 + 航次；挂靠港在 Voyage.Ports 里）</summary>
    public Voyage Voyage { get; set; } = new();

    /// <summary>本票装货港 UN/LOCODE，如 CNTAO。每票各自不同 —— 同船同航次的两票可走不同港</summary>
    public string Pol { get; set; } = "";

    /// <summary>本票卸货港 UN/LOCODE，如 USLAX。存在提单上而非航次上，故不会互相覆盖</summary>
    public string Pod { get; set; } = "";

    /// <summary>最终目的地 UN/LOCODE（内陆点），如 USCHI 芝加哥；每票货各自不同，可空</summary>
    public string FinalDestination { get; set; } = "";

    /// <summary>开船日期，由「装货港」对应挂靠港的开航日带出（不落本表，读写时即时解析）</summary>
    public DateTime? EtdUtc { get; set; }

    /// <summary>到港日期，由「卸货港」对应挂靠港的到港日带出（不落本表，读写时即时解析）</summary>
    public DateTime? EtaUtc { get; set; }

    /// <summary>该提单下的节点（货级：截关、免箱期…）</summary>
    public List<Milestone> Milestones { get; set; } = new();
}
