

namespace Cdk.Config;

public class ExampleLocalConfig : IAppConfig
{
    public string EnvName { get; set; } = "local"; //local can be configured using something like Environment.MachineName;
    public string AppName { get; set; } = "app";
    public int OpenSearchVolumeSize { get; set; } = 10;
    public string OpenSearchDataNodeInstanceType { get; set; } = "t3.small.search";
    public string OpenSearchMasterNodeInstanceType { get; set; } = "t3.small.search";
    public int OpenSearchDataNodes { get; set; } = 1;
    public int OpenSearchMasterNodes { get; set; } = 0;
    public bool OpenSearchMultiAz { get; set; } = false;
}
