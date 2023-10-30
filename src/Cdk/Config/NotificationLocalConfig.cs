
namespace Cdk.Config;

public class NotificationLocalConfig : IServiceConfig
{
    public string EnvName { get; set; } = "local"; //local can be configured using something like Environment.MachineName;
    public string AppName { get; set; } = "app";
    public string ServiceName { get; set; } = "notification";
}