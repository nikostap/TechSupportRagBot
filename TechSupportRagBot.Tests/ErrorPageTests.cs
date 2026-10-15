using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Pages;

namespace TechSupportRagBot.Tests;

public sealed class ErrorPageTests
{
    [Fact]
    public void Ajax_error_returns_safe_json_with_http_500()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Accept = "application/json";
        context.TraceIdentifier = "request-123";
        var page = new ErrorModel { PageContext = new PageContext { HttpContext = context } };

        var result = Assert.IsType<JsonResult>(page.OnPost());
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.DoesNotContain("exception", System.Text.Json.JsonSerializer.Serialize(result.Value), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("request-123", page.RequestId);
    }
}
