using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace MCPExtension.Core
{
    public interface IApiHandler
    {
        string Path { get; }
        string Method { get; }
        Task HandleAsync(HttpContext context);
    }
}
