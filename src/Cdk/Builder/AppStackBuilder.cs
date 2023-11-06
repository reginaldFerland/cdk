
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.OpenSearchService;
using Amazon.CDK.AWS.SSM;
using Cdk.Config;
using Constructs;

namespace Cdk.Builder;


class AppStackBuilder
{
    // Class members here
    private readonly Stack _stack;
    private readonly IAppConfig _config;
    private readonly string _appName;
    private readonly string _envName;
    private readonly string _parameterStoreBase;
    internal AppStackBuilder(Construct scope, IAppConfig config)
    {
        // Constructor logic here
        _config = config;
        _stack = new Stack(scope, $"{_config.AppName}-general-{_config.EnvName}");
        _parameterStoreBase = $"/{_config.EnvName}/{_config.AppName}/general";
        _appName = _config.AppName;
        _envName = _config.EnvName;
    }

    internal AppStackBuilder WithOpenSearch()
    {
        // Method logic here
        var domainName = new StringBuilder();
            domainName.Append(_appName).Append("-opensearch-").Append(_envName);

        var opensearch = new Domain(_stack, domainName.ToString(), new DomainProps 
        {
            DomainName = domainName.ToString(),
            Version = EngineVersion.OPENSEARCH_2_9,
            EncryptionAtRest = new EncryptionAtRestOptions {
                Enabled = true,
            },
            Ebs = new EbsOptions {
                Enabled = true,
                VolumeSize = _config.OpenSearchVolumeSize,
                VolumeType = Amazon.CDK.AWS.EC2.EbsDeviceVolumeType.GP3
            },
            ZoneAwareness = new ZoneAwarenessConfig {
                Enabled = false,
            },
            EnforceHttps = true,
            NodeToNodeEncryption = true,
            Capacity = new CapacityConfig {
                DataNodeInstanceType = _config.OpenSearchDataNodeInstanceType,
                MasterNodeInstanceType = _config.OpenSearchMasterNodeInstanceType,
                DataNodes = _config.OpenSearchDataNodes,
                MasterNodes = _config.OpenSearchMasterNodes,
                MultiAzWithStandbyEnabled = _config.OpenSearchMultiAz,
            },
        });

        // Set general parmeter store values
        var param = new StringParameter(_stack, "OpenSeachUrlParam", new StringParameterProps {
            ParameterName = _parameterStoreBase + "/OpenSeachUrl",
            StringValue = opensearch.DomainEndpoint
        });
 
        return this;
    }

    internal AppStackBuilder WithContainerCluster(out Cluster containerService, Vpc vpc)
    {
        var serviceName = new StringBuilder();
            serviceName.Append(_appName).Append("-service-").Append(_envName);

        containerService = new Cluster(_stack, serviceName.ToString(), new ClusterProps {
            Vpc = vpc,
            ClusterName = serviceName.ToString(),
            ContainerInsights = true,
        });

        // We can configure auto scaling or use containerService.AddCapacity() to add capacity to the cluster that gets returned

        return this;
    }

    // Create VPC
    internal AppStackBuilder WithVpc(out Vpc vpc)
    {
        var vpcName = $"{_appName}-vpc";

        var props = new VpcProps {
            IpAddresses = IpAddresses.Cidr("10.10.0.0/16"),
            ReservedAzs = 2,
            VpcName = vpcName,
            SubnetConfiguration = new [] {
                new SubnetConfiguration {
                    CidrMask = 24,
                    Name = "public",
                    SubnetType = SubnetType.PUBLIC,
                },
                new SubnetConfiguration {
                    CidrMask = 24,
                    Name = "private",
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                },
                new SubnetConfiguration {
                    CidrMask = 20,
                    Name = "Isolated",
                    SubnetType = SubnetType.PRIVATE_ISOLATED,
                },
            },
        };

        vpc = new Vpc(_stack, vpcName, props);

        return this;
    }


    // Create API Gateway

}