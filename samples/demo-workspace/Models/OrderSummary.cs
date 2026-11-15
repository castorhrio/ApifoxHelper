namespace Demo.Api.Models;

/// <summary>
/// 订单概要。
/// </summary>
public class OrderSummary
{
    /// <summary>
    /// 订单号。
    /// </summary>
    public string OrderNo { get; set; } = "";

    /// <summary>
    /// 订单总金额，单位：元。
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// 订单状态：paid 已支付、pending 待支付。
    /// </summary>
    public string Status { get; set; } = "";
}
