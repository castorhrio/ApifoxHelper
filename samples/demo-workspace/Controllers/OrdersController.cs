using Demo.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

/// <summary>
/// 订单查询接口。演示工作区的两个接口都在这里。
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    /// <summary>
    /// 按订单号查询单个订单。
    /// </summary>
    /// <param name="id">订单号，形如 SO-20260101-0001。</param>
    /// <returns>订单概要；订单不存在时返回 404。</returns>
    [HttpGet("{id}")]
    public ActionResult<OrderSummary> GetOrder(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        return Ok(new OrderSummary { OrderNo = id, TotalAmount = 128.50m, Status = "paid" });
    }

    // 演示缺口 ①：这个方法没有 /// 注释——扫描会把它报成「注释缺口」。
    // 演示时这一处就是「3 分钟看到价值」的第一眼：工具自己找出来，不用人翻代码。
    [HttpGet]
    public ActionResult<IReadOnlyList<OrderSummary>> ListOrders([FromQuery] string? status)
    {
        var all = new List<OrderSummary>
        {
            new() { OrderNo = "SO-20260101-0001", TotalAmount = 128.50m, Status = "paid" },
            new() { OrderNo = "SO-20260101-0002", TotalAmount = 66.00m, Status = "pending" },
        };
        return Ok(string.IsNullOrWhiteSpace(status)
            ? all
            : all.Where(o => o.Status == status).ToList());
    }
}
