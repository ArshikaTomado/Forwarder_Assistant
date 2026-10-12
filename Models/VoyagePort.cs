// VoyagePort.cs —— 航次挂靠港（照 ERP「出口航次计划」的挂靠港子表）
namespace ForwarderAssistant.Models;

/// <summary>
/// 一个航次里的一个挂靠港：港口、到港 / 开航日期、截关 / 截箱日期、码头、类别。
/// 一个航次可有多条（同船同航次去多个港），故港口不进水唯一键。
/// </summary>
public class VoyagePort
{
    /// <summary>数据库主键</summary>
    public long Id { get; set; }

    /// <summary>所属航次 id</summary>
    public long VoyageId { get; set; }

    /// <summary>港口代码 UN/LOCODE，如 CNTAO</summary>
    public string PortCode { get; set; } = "";

    /// <summary>港口名称，如 QINGDAO, CHINA</summary>
    public string PortName { get; set; } = "";

    /// <summary>到港日期（UTC），null = 未设</summary>
    public DateTime? ArrivalUtc { get; set; }

    /// <summary>开航日期（UTC），null = 未设</summary>
    public DateTime? DepartureUtc { get; set; }

    /// <summary>截关日期（UTC），null = 未设</summary>
    public DateTime? CutoffUtc { get; set; }

    /// <summary>截箱日期（UTC），null = 未设</summary>
    public DateTime? CyCutoffUtc { get; set; }

    /// <summary>停靠码头</summary>
    public string Terminal { get; set; } = "";

    /// <summary>港口类别标记，ERP 口径：Or 起运 / Lo 装货 / Di 卸货 / De 目的 / La 最后外港 / Fu 首靠美港</summary>
    public string Flags { get; set; } = "";

    /// <summary>挂靠顺序（同航次内排序用）</summary>
    public int Seq { get; set; }
}
