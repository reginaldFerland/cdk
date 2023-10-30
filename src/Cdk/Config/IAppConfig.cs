
namespace Cdk.Config;

public interface IAppConfig : IEnvConfig
{
    // Interface members here
    public int OpenSearchVolumeSize { get; set; }
    public string OpenSearchDataNodeInstanceType { get; set; }
    public string OpenSearchMasterNodeInstanceType { get; set; }
    public int OpenSearchDataNodes { get; set; }
    public int OpenSearchMasterNodes { get; set; }
    public bool OpenSearchMultiAz { get; set; }
}

