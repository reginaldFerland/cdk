
namespace Cdk.Config;

public interface IServiceConfig : IEnvConfig
{
    // Interface members here
    public string ServiceName { get; set; }
}