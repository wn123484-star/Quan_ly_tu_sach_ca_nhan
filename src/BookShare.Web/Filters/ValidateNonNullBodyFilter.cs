using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BookShare.Web.Filters;

/// <summary>
/// [CORE-VALIDATION] Bộ lọc kiểm tra toàn cục đối với dữ liệu JSON gửi qua <c>[FromBody]</c>.
/// Ngăn chặn lỗi hệ thống HTTP 500 (NullReferenceException) khi máy khách gửi thân yêu cầu rỗng,
/// chuỗi <c>null</c> hoặc JSON sai kiểu dữ liệu bằng cách trả về phản hồi HTTP 400 chuẩn tiếng Việt.
/// </summary>
public sealed class ValidateNonNullBodyFilter : IActionFilter
{
    private static readonly NullabilityInfoContext NullabilityContext = new();

    /// <summary>
    /// Kiểm tra trước khi thực thi Action trong Controller: nếu bất kỳ tham số <c>[FromBody]</c>
    /// không cho phép null nào bị bind thành <c>null</c>, chặn ngay và trả JSON HTTP 400 thân thiện.
    /// </summary>
    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var parameter in context.ActionDescriptor.Parameters)
        {
            if (parameter.BindingInfo?.BindingSource != BindingSource.Body)
            {
                continue;
            }

            bool isNullableParam = false;
            if (parameter is ControllerParameterDescriptor cpd)
            {
                if (Nullable.GetUnderlyingType(cpd.ParameterInfo.ParameterType) != null)
                {
                    isNullableParam = true;
                }
                else
                {
                    var nullInfo = NullabilityContext.Create(cpd.ParameterInfo);
                    isNullableParam = nullInfo.ReadState == NullabilityState.Nullable;
                }
            }

            if (isNullableParam)
            {
                continue;
            }

            bool hasArg = context.ActionArguments.TryGetValue(parameter.Name, out var value);
            if (!hasArg || value == null)
            {
                string requestId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                context.Result = new BadRequestObjectResult(new
                {
                    success = false,
                    statusCode = StatusCodes.Status400BadRequest,
                    title = "Dữ liệu gửi lên chưa hợp lệ",
                    message = "Nội dung yêu cầu bị trống hoặc sai định dạng dữ liệu. Vui lòng kiểm tra lại các trường thông tin và thử lại.",
                    requestId
                });
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // Không cần xử lý hậu kỳ
    }
}
