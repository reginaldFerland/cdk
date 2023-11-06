using System.Collections.Generic;
using System.Text;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Ecr.Assets;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.SSM;
using Cdk.Config;
using Constructs;

namespace Cdk.Builder;

public class ServiceStackBuilder
{
    private readonly Stack _stack;
    private readonly string _parameterStoreBase;
    private readonly string _appName;
    private readonly string _serviceName;
    private readonly string _envName;
    private readonly IServiceConfig _config;
    private DatabaseCluster _databaseCluster;
    private Repository _containerRegistry;

    internal ServiceStackBuilder(Construct scope, IServiceConfig config)
    {
        _config = config;
        _stack = new Stack(scope, $"{_config.AppName}-{_config.ServiceName}-{_config.EnvName}");
        _parameterStoreBase = $"/{_config.EnvName}/{_config.AppName}/{_config.ServiceName}";
        _appName = _config.AppName;
        _serviceName = _config.ServiceName;
        _envName = _config.EnvName;
    }

    // Add paramstore stuff
    internal ServiceStackBuilder WithDatabaseCluster(Credentials credentials = null)
    {
        var databaseClusterName = new StringBuilder();
            databaseClusterName.Append(_appName).Append("-").Append(_serviceName).Append("-db-").Append(_envName);

        var databaseClusterEngine = DatabaseClusterEngine.AuroraPostgres(new AuroraPostgresClusterEngineProps {
            Version = AuroraPostgresEngineVersion.VER_15_3,
        });

        // I can create a database from a snapshot, preserving data 
        _databaseCluster = new DatabaseCluster(_stack, databaseClusterName.ToString(), new DatabaseClusterProps {
            Engine = databaseClusterEngine,
            RemovalPolicy = RemovalPolicy.DESTROY,
            ServerlessV2MinCapacity = 1,
            ServerlessV2MaxCapacity = 4, // Add max capacity
            DefaultDatabaseName = _serviceName + "_database",
            StorageEncrypted = true,
            Backup = new BackupProps {
                Retention = Duration.Days(7),
            },
            DeletionProtection = false,
            Credentials = credentials ?? Credentials.FromPassword("postgres", SecretValue.UnsafePlainText("postgres")), // Fix this
            Vpc = new Vpc(_stack, "Vpc", new VpcProps {
                MaxAzs = 2,
                NatGateways = 1,
                SubnetConfiguration = new [] {
                    new SubnetConfiguration {
                        Name = "public",
                        SubnetType = SubnetType.PUBLIC,
                    },
                    new SubnetConfiguration {
                        Name = "private",
                        SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    },
                },
            }),
            Writer = ClusterInstance.ServerlessV2("writer", new ServerlessV2ClusterInstanceProps {
                
            }),
        });

        // Create SSM parameter for database 
        var param = new StringParameter(_stack, "DatabaseClusterArn", new StringParameterProps {
            ParameterName = _parameterStoreBase + "/DatabaseEndpoint",
            StringValue = _databaseCluster.ClusterEndpoint.ToString()
        });
        return this;
    }

    internal ServiceStackBuilder WithClusterInstance(string instanceName = null, IClusterInstanceBindOptions options = null)
    {
        instanceName ??= "reader-" + _databaseCluster.InstanceIdentifiers.Length;
        options ??= new ClusterInstanceBindOptions();

        var instance = ClusterInstance.ServerlessV2(instanceName);

        instance.Bind(_stack, _databaseCluster, options);

        return this;
    }

    internal ServiceStackBuilder WithSnsTopic(string topicName, out Topic topic)
    {
        topic = new Topic(_stack, $"{_config.AppName}-${_config.ServiceName}-${topicName}-${_config.EnvName}", new TopicProps {
            TopicName = topicName,
        });

        var param = new StringParameter(_stack, $"{topicName}ArnParameter", new StringParameterProps {
            ParameterName = _parameterStoreBase + $"/{topicName}Arn",
            StringValue = topic.TopicArn
        });

        return this;
    }

    internal ServiceStackBuilder WithSnsTopic(string topicName)
    {
        return WithSnsTopic(topicName, out _);
    }

    internal ServiceStackBuilder WithSqsQueue(string queueName, out Queue queue)
    {
        queue = new Queue(_stack, $"{_config.AppName}-${_config.ServiceName}-${queueName}-${_config.EnvName}", new QueueProps {
            QueueName = queueName,
        });

        var param = new StringParameter(_stack, $"{queueName}UrlParameter", new StringParameterProps {
            ParameterName = _parameterStoreBase + $"/{queueName}Url",
            StringValue = queue.QueueUrl
        });

        return this;
    }

    internal ServiceStackBuilder WithSqsQueue(string queueName)
    {
        return WithSqsQueue(queueName, out _);
    }

    internal ServiceStackBuilder WithSubscription(Topic topic, Queue queue)
    {
        topic.AddSubscription(new SqsSubscription(queue));

        return this;
    }

    internal ServiceStackBuilder WithContainerRegistry()
    {
        var registryName = new StringBuilder();
            registryName.Append(_appName).Append("-registry-").Append(_envName);

        _containerRegistry = new Repository(_stack, registryName.ToString(), new RepositoryProps {
            RepositoryName = registryName.ToString(),
            RemovalPolicy = RemovalPolicy.DESTROY,
            Encryption = RepositoryEncryption.KMS,
            ImageScanOnPush = true,
            LifecycleRules = new LifecycleRule[] {
                new() {
                    MaxImageCount = 3,
                    TagStatus = TagStatus.ANY,
                }
            },
            ImageTagMutability = TagMutability.IMMUTABLE
        });

        // Paramstore - Do we need the URI or just set a url per env?
        var param = new StringParameter(_stack, "RegistryParam", new StringParameterProps {
            ParameterName = _parameterStoreBase + "/RegistryUri",
            StringValue = _containerRegistry.RepositoryUri
        });
 
        return this;
    }

    internal ServiceStackBuilder WithService(Cluster cluster)
    {
        var taskName = $"{_config.AppName}-{_config.ServiceName}-task-{_config.EnvName}";

        var task = new FargateTaskDefinition(_stack, taskName, new FargateTaskDefinitionProps {
            Cpu = 256,
            MemoryLimitMiB = 512,
            RuntimePlatform = new RuntimePlatform {
                OperatingSystemFamily = OperatingSystemFamily.LINUX,
                CpuArchitecture = CpuArchitecture.X86_64
            }
        });


        ContainerImage image;

        var initialUpload = 1;
        if(initialUpload == 0)
        {
            image = ContainerImage.FromEcrRepository(_containerRegistry);
        }
        else
        {
            // Make docker image
            var docker = new DockerImageAsset(_stack, "DockerImage", new DockerImageAssetProps {
                Directory = @"C:\Users\Reginald\source\repos\HelloWorld",
                File = @"HelloWorld\Dockerfile",
                BuildArgs = new Dictionary<string, string> {
                    { "BUILD_CONFIGURATION", "Release" }
                },
            });

            image = ContainerImage.FromDockerImageAsset(docker);
        }

        var containerName = $"{_config.AppName}-{_config.ServiceName}-container-{_config.EnvName}";

        task.AddContainer(containerName, new ContainerDefinitionOptions {
            Image = image,
            PortMappings = new PortMapping[] {
                new() {
                    ContainerPort = 80,
                    HostPort = 80,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
                },
                new() {
                    ContainerPort = 443,
                    HostPort = 443,
                    Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP
                }
            },
            HealthCheck = new HealthCheck {
                Command = new [] { "CMD-SHELL", "curl -f http://localhost/health/ready || exit 1" },
                Interval = Duration.Seconds(5),
                Timeout = Duration.Seconds(2),
                Retries = 3,
                StartPeriod = Duration.Seconds(10),
            },
            ReadonlyRootFilesystem = true,
            Environment = new Dictionary<string, string> {
                //{ "ASPNETCORE_ENVIRONMENT", _config.EnvName }, // This could be cool
                {"COMPlus_EnableDiagnostics", "0"},
            },
        });

        var serviceName = $"{_config.AppName}-{_config.ServiceName}-service-{_config.EnvName}";

        var service = new FargateService(_stack, serviceName, new FargateServiceProps {
            Cluster = cluster,
            TaskDefinition = task,
            DesiredCount = 1,
            VpcSubnets = new SubnetSelection {
                SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
            },
            //AssignPublicIp = false,
        });

        return this;
    }
}
