// Voyage.cs —— 航次实体：以「船名 + 航次」唯一标识（照 ERP 的三层结构）
namespace ForwarderAssistant.Models;

/// <summary>
/// 航次：同一条船、同一个航次唯一一份。一条船一次会挂靠多个港口，
/// 这些港口存在 <see cref="Ports"/> 子表里（港口不进唯一键），
/// 各提单再各自指明自己的装货港 / 卸货港。
/// </summary>
public class Voyage
{
    /// <summary>数据库主键</summary>
    public long Id { get; set; }

    /// <summary>船名，如 EVER FOCUS</summary>
    public string VesselName { get; set; } = "";

    /// <summary>航次，如 1100E</summary>
    public string VoyageNo { get; set; } = "";

    /// <summary>该航次挂靠的港口（读库时随航次一并带出）</summary>
    public List<VoyagePort> Ports { get; set; } = new();
}
