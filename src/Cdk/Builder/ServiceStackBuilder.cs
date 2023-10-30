using System.Text;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
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

    internal Stack Build()
    {
        return _stack;
    }
}
