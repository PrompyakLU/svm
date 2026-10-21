---
inclusion: always
---

# IRPC AI-DLC workshop: AWS environment rules (Container account 337909773380)

Version 3 (Day 1-3). If your copy has no Part C, replace it with this version.

Application design (frameworks, languages, project structure, testing, coding standards) follows the separate IRPC "Web Application Tech Stack Standard" steering file. This file governs only where and how things run on AWS. Where both files touch a topic, follow the Tech Stack Standard for application design, but this file wins on anything that depends on this AWS environment: authentication provider, database edition, TLS, hosting and regions. In particular, of the providers the Tech Stack Standard allows, only OIDC against the shared workshop Cognito pool is available (section 8). Microsoft Entra ID app registrations and Windows Authentication are not available in the workshop. Hosting: containers on ECS Fargate in AWS account 337909773380 replace IIS hosting. Never deploy to IRPC on-prem servers, IIS or any non-AWS host. Laptop development is fine.

## Part A - AWS environment rules

### 1. Account and sign-in

- Account `337909773380` (IRPC_AWS Container). Home region `ap-southeast-7` (Thailand).
- Sign in at https://sgawsirpc.awsapps.com/start and choose permission set `IRPC-WS-Participant` (session 10 hours). For the CLI and SDKs: `aws configure sso` (start URL above, SSO region `ap-southeast-1`), then pass `--profile <your profile>` on every command.
- First-time CLI setup (AWS CLI v2): run `aws configure sso` and answer: session name `irpc`, start URL `https://sgawsirpc.awsapps.com/start`, SSO region `ap-southeast-1`, scopes: accept the default, account `337909773380` and role `IRPC-WS-Participant` (chosen automatically when they are your only ones), default region `ap-southeast-7`, output `json`, profile name `irpc-ws`.
- Every day, or after `Token has expired`: `aws sso login --profile irpc-ws`. Check with `aws sts get-caller-identity --profile irpc-ws`.
- Local code gets credentials from the profile: set `AWS_PROFILE=irpc-ws` in the shell or in an uncommitted `.env` or launch setting. In code, use the SDK default credential chain and never hardcode a profile, so the same code uses the task role on AWS. .NET needs the NuGet packages `AWSSDK.SSO` and `AWSSDK.SSOOIDC` for SSO profiles.
- Never create IAM users or access keys, and never put keys in code or `.env`.
- On the IRPC/Metro network, errors such as `CERTIFICATE_VERIFY_FAILED`, `self-signed certificate in certificate chain` or `UNABLE_TO_GET_ISSUER_CERT_LOCALLY` come from TLS inspection. Use the CA file your facilitator gives you, for that one command or process only: `AWS_CA_BUNDLE` for the AWS CLI and Python, `NODE_EXTRA_CA_CERTS` for Node.js (AWS SDK v3, Prisma, npm). .NET on Windows uses the Windows certificate store. Never turn off certificate verification (no `NODE_TLS_REJECT_UNAUTHORIZED=0`, `--no-verify-ssl` or `verify=False`).
- Kiro IDE uses your Kiro Pro seat in account `277707130330`; nothing to set up here. Never create a Kiro or Amazon Q Developer profile or subscription in `337909773380` (it is denied).
- Your access is guarded. An AccessDenied usually means a name, tag, size or region rule below was not followed. Fix the request and don't look for a way around it.

### 2. Workshop days

- Day 1 (8 Oct): build and run on your laptop (SQLite or `docker compose`) and call Bedrock (section 7). No AWS deployment on Day 1.
- Day 2 and Day 3 (15 and 21 Oct): deploy containers to ECS Fargate following the deploy rules in Part C.

### 3. Regions, network and what runs where

| Region | Use for |
|---|---|
| `ap-southeast-7` | Everything in a VPC: ECS Fargate tasks, internal ALB, VPC Lambda, SageMaker jobs, CodeBuild; plus ECR, S3, DynamoDB, Secrets Manager, CloudWatch Logs |
| `us-east-1` | Bedrock only (section 7) |
| `ap-southeast-1` | Only non-VPC services missing in `ap-southeast-7` (for example the SES API), non-sensitive data only |

- Every other region is denied. Never use the default VPCs of `ap-southeast-1` or `us-east-1` (denied).
- All workshop networking is in the Container VPC `vpc-035932c87561920bc` (10.24.136.0/21, `ap-southeast-7`), owned by the IRPC network account. You can't create or change VPCs, subnets, routes, gateways or endpoints.

| What | Subnets |
|---|---|
| Internal ALB (one per use case) | Container-WebApp-Subnet-01 `subnet-0862140e198a4204d` (7a), Container-WebApp-Subnet-02 `subnet-03dc693d79804e293` (7b) |
| Fargate tasks, VPC Lambda, SageMaker jobs, CodeBuild with a VPC | Container-FileShare-Subnet-01 `subnet-0adbd517ab1d0e221`, Container-FileShare-Subnet-02 `subnet-0180ec2800b321ebd`; overflow Container-SAP-Subnet-01 `subnet-07e7dd6fb62056ed0`, Container-SAP-Subnet-02 `subnet-0924d0ced23269412` |
| Shared RDS SQL Server (admin only) | DB subnet group `irpc-ws-db-subnets`: Container-DB-Subnet-01 `subnet-05f74aeeac4aeb214`, Container-DB-Subnet-02 `subnet-01745230ad99c057f` |

- Ingress is internal-only: users reach apps from the IRPC network or VPN through an internal ALB. No public IPs, internet-facing load balancers or other public endpoints.
- Outbound internet goes through the IRPC firewall (HTTPS/HTTP).
- On-prem systems (SAP, LIMS, PHD, DW, HR Portal, ASTM, TAS, E-Health, iServe, AD) are NOT reachable from AWS. Every team uses mock data:
  - Build every external-system call behind an interface/adapter with a mock implementation plus seed data (CSV/Excel/JSON, or tables in your team's own database), so the real API can replace the mock later without touching business logic.
  - Never put real personal data in mock data.
- Email: send only through HTTPS APIs (the Amazon SES API in `ap-southeast-1`, or Microsoft Graph sendMail). No SMTP (ports 25, 465, 587) from the Container VPC.

### 4. Naming and tags

- AWS resource names: `irpc-ws-{usecase}-{resource}-{owner}`. `owner` is the local part of your UPN, lowercase, with dots turned into hyphens (`somchai.j@irpc.co.th` -> `somchai-j`). Example: `irpc-ws-rcm-api-somchai-j`.
- CloudFormation/CDK stacks, Lambda functions, S3 buckets, DynamoDB tables, secrets, SSM parameters (`/irpc-ws/...`), SNS topics, SQS queues, state machines, EventBridge rules, schedules, ECR repositories, CodeBuild projects and IAM roles must start with `irpc-ws-`. ECS clusters too: any other cluster name, including `default`, is denied. Name ECS services and task definitions the same way.
- Required tags on everything you create (direct API calls are denied without them; stacks are denied without them as stack tags):

| Tag | Value |
|---|---|
| `Environment` | `workshop` |
| `Owner` | Your IAM Identity Center user name, exactly as shown in the access portal (e.g. `somchai.j@irpc.co.th`). Case-sensitive |
| `UseCase` | Your team's code from the list below (any other value is denied) |

- `Group` is optional (facilitators only); you don't need it.
- `Migrate=prod` or `Migrate=nonprod` marks something IRPC may move after the workshop. Set it only after facilitator approval; unapproved Migrate tags are removed at teardown.
- UseCase codes: `customer-data-collection`, `survey`, `rpa-optimization`, `ea-system`, `rcm`, `failure-classification`, `hr-analytics`, `chemical-management`, `transport-waste-scrap`, `internship-recruitment`, `sulfur-prediction`, `bad-actor`, `asset-survey`, `data-quality`, `gr-po-pending`, `kpali-talk`, `mlarena`, `other-1`, `other-2`, `other-3`.
- A project that is not one of the 17 IRPC use cases takes a free `other-1`, `other-2` or `other-3` (agree it with the facilitators) and names its resources `irpc-ws-other-N-<project>-...`, e.g. `irpc-ws-other-2-quiz-api-somchai-j`.

### 5. Allowed services and sizes

- Containers: ECS on Fargate only (FARGATE or FARGATE_SPOT), at most 2 vCPU and 8 GiB per task. Images go to ECR repositories `irpc-ws-*`. Services must propagate tags to their tasks.
- Image builds: Docker on your laptop, or a CodeBuild project `irpc-ws-*` with BUILD_GENERAL1_SMALL or BUILD_GENERAL1_MEDIUM and a service role `irpc-ws-*`.
- Database: On AWS, every team uses its own database on one shared, admin-managed RDS SQL Server instance; do not create your own SQL Server instance. Details in Part C.
  - One database per use case. You can't create any RDS instance or cluster yourself (any engine).
  - Node.js teams use Prisma with provider `sqlserver`; .NET teams use EF Core with the SqlServer provider. Both connect to their own database on the shared instance (connection details in Part C).
  - Day 1: SQLite, or SQL Server in `docker compose` on your laptop.
- Serverless: Lambda (VPC Lambda in the FileShare or SAP subnets) as an internal ALB target, DynamoDB, S3, Step Functions, EventBridge Scheduler (`irpc-ws-{usecase}-{JobName}`).
- SageMaker training, processing and transform jobs on ml.m6i.large, ml.m6i.xlarge, ml.c6i.xlarge, ml.m7i.large or ml.m7i.xlarge; endpoints on ml.c5.large, ml.m5.large or ml.t2.medium. No Studio or notebook instances.
- Denied: EC2 instances, EKS, ElastiCache/Redis, Aurora and other DB clusters, OpenSearch, API Gateway, CloudFront, Lambda function URLs, public S3 buckets and websites, internet-facing load balancers, AppSync, Transfer Family, Bedrock AgentCore, ECS Express Mode, App Runner, CodePipeline/CodeConnections, service auto scaling, certificate changes (the admin provides one shared certificate), Kiro/Q subscriptions.

### 6. IAM

- Every role you create is named `irpc-ws-*` and carries the permissions boundary `arn:aws:iam::337909773380:policy/irpc-ws-boundary`. Creating a role without it is denied.
- Trust only AWS service principals (for example `ecs-tasks.amazonaws.com`, `lambda.amazonaws.com`, `codebuild.amazonaws.com`). Assuming roles is denied for you and for your roles.

### 7. Bedrock

All models are on-demand in `us-east-1`:

| Use | Model ID |
|---|---|
| Default text | `amazon.nova-lite-v1:0` |
| Simple, cheap text | `amazon.nova-micro-v1:0` |
| Harder reasoning, Thai/Pali translation | `amazon.nova-pro-v1:0` or `openai.gpt-oss-120b-1:0` |
| Smaller open-weight model | `openai.gpt-oss-20b-1:0` |
| Embeddings (default) | `amazon.titan-embed-text-v2:0` |
| Embeddings, multimodal (optional) | `amazon.nova-2-multimodal-embeddings-v1:0` |

- Use the AWS SDK and the Converse API for text (InvokeModel for embeddings). Read the region and model from configuration: `Bedrock__Region` (`us-east-1`) and `Bedrock__ModelId`, so changing a model is a configuration change.
- From Day 2 the value of `Bedrock__ModelId` on AWS becomes the published value for your use case and model: an application inference profile ARN, or the plain model ID for a model without a profile (given in Part C). Pass the setting unchanged as `modelId` and never parse it or branch on its text. If code needs to know the model family (for example to build an embeddings request body), read it from a separate setting.
- gpt-oss models return a reasoning block before the answer, and reasoning tokens count against `maxTokens`. Set `maxTokens` generously (for example 2000) and read the `text` content block, not `reasoningContent`.
- Call models only through the AWS SDK `bedrock-runtime` Converse/InvokeModel API in `us-east-1`, not an OpenAI-compatible endpoint. The `bedrock-mantle` endpoint (the OpenAI SDK's Bedrock default) is denied.
- Every other model, every cross-region inference profile ID, fine-tuning, provisioned throughput, model import and copy, batch inference and model evaluation jobs, and custom models are denied. No Claude, no Cohere.
- All Bedrock processing is in the US; no personal or sensitive data in prompts.
- On Day 1 your laptop code calls Bedrock with your SSO profile; on AWS it uses the task or Lambda role.

### 8. Authentication

- Read the OIDC settings (authority, client ID, callback path) from configuration. The shared Cognito pool in `337909773380` and the per-use-case client IDs are in Part C.
- Microsoft Entra ID and Windows Authentication are not available.

### 9. Secrets and configuration

- Configuration goes in environment variables or appsettings. Secrets go in Secrets Manager under `irpc-ws-...` names; read them with `GetSecretValue`, one call per secret (`BatchGetSecretValue` is denied).
- Never commit secrets, and never put them in images, plaintext task-definition environment variables or logs.

### 10. Data protection

- No real personal or health data unless IRPC approves it (PDPA). If approved, tag it `DataClassification=pnc`, keep it out of Bedrock prompts and public endpoints, and use the Part B `pnc_` naming.
- SAP, HR and sensor data stays in `ap-southeast-7`.

### 11. Cost hygiene

- Budgets are per use case. Container budgets are cumulative for Q4 2026: October, November and December count against one limit.
- Scale your ECS services to 0 when you finish for the day and between workshop days. Delete tasks, images, buckets and log groups you no longer need. Delete SageMaker endpoints the same day.
- When the account budget reaches its limit, creating or starting resources and calling Bedrock are frozen for all participants (stop and delete keep working).

### 12. Do NOT

- Never deploy to IRPC on-prem servers, IIS or any non-AWS host.
- Never run a database (SQL Server, PostgreSQL, MySQL, Redis) in a container on AWS; use your database on the shared RDS instance (`irpc-ws-db-subnets`).
- Use cross-region inference profile IDs, public endpoints or internet-facing load balancers.
- Create a Kiro or Q Developer profile or subscription in `337909773380`.
- Create VPCs, subnets, internet/NAT gateways, Elastic IPs, VPC endpoints or routes, or open `0.0.0.0/0` ingress.
- Touch resources without the `irpc-ws-` prefix, or the admin resources `irpc-ws-admin-*`, `irpc-ws-budget-alerts` and `irpc-ws-ops-alerts` (reading them is fine). Never name your own resources `irpc-ws-admin-*`.
- Buy reservations, savings plans or marketplace products.
- Store secrets in code, images or plaintext task-definition environment variables (section 9), or load personal data without IRPC approval.

## Part B - IRPC naming standard (application and database objects)

IRPC Standard Naming Rev 0 (Digital). Use these forms exactly.

| Object | Rule | Example |
|---|---|---|
| Database | System name, lowerCamelCase | `fssEx` |
| Table | `master_` / `config_` / `trans_` + purpose in lowerCamelCase | `master_glAccount` |
| Column (workshop default) | lowerCamelCase | `glAccountCode` |
| View / stored procedure, single table | `uv_` / `usp_` + table name in lowerCamelCase | `uv_masterGl` |
| View / stored procedure, multiple tables | `uv_` / `usp_` + `{SYSTEM}_` + purpose in PascalCase | `usp_FSS_ReportAcc` |
| Variables, objects (C#, TypeScript/JavaScript) | camelCase | `vendorCount` |
| Classes, methods | PascalCase | `VendorService.GetVendors` |
| API JSON fields | camelCase | `"vendorName"` |
| API routes (workshop default) | `/api/{resource}`, lowercase plural | `/api/vendors` |
| Job | `{Activity}_{Purpose}_{Period}`. Activity `Create`/`Read`/`Update`/`Delete`. Period `_{n}Second`, `_{n}Minute`, `_{n}Hour`, `_Daily`, `_Weekly`, `_Yearly`; no schedule = trailing `_` | `Create_VendorMaster_2Hour` |
| AWS schedule for a job | `irpc-ws-{usecase}-{JobName}` | `irpc-ws-rcm-Create_VendorMaster_2Hour` |
| Linked database (data integration) | `LDB_{System}_{Purpose}`, System in PascalCase | `LDB_OpenLIMS_QAS` |
| Integration stored procedure | `usp_intf_` + `{sourceType}{Activity}{DataArea}{Purpose}` in lowerCamelCase; Activity `Load` (full) or `Merge` | `usp_intf_excelMergeHrData` |
| Data service | `getdwusp_` (procedure) / `getdwufn_` (function) / `getdwuv_` (view), then `pnc_` only for personal data, then `{source or data area}_{purpose}` | `getdwusp_pnc_hr_genLeOtByEMP` |
| System Level | `P` Production, `S` Staging, `T` Test, `D` Development (workshop = `D`) | `SystemLevel=D` |

EF Core: map every entity explicitly with an `IEntityTypeConfiguration<T>` and `ToTable("master_glAccount")`, without pluralized default table names. Map columns to lowerCamelCase with `HasColumnName` or one naming convention.

## Part C - Deploy on AWS (Day 2 and Day 3)

From Day 2 you deploy your own stack to ECS Fargate in `337909773380`. Part A still applies in full; this part adds the deploy facts.

### C1. Values and parameters

| What | Value |
|---|---|
| Shared ALB certificate (self-signed) | `arn:aws:acm:ap-southeast-7:337909773380:certificate/386efed8-215d-4d03-aa67-34b2379da1f6` |
| Cognito user pool | `ap-southeast-7_nPA2oYWMe` |
| Cognito hosted UI domain | `https://irpc-ws-container.auth.ap-southeast-7.amazoncognito.com` |
| OIDC authority (issuer) | `https://cognito-idp.ap-southeast-7.amazonaws.com/ap-southeast-7_nPA2oYWMe` |

Everything else comes from SSM Parameter Store in `ap-southeast-7` (String). Read it in IaC or at startup; never copy IDs into code.

| Parameter | Value |
|---|---|
| `/irpc-ws/shared/network/vpc-id`, `/irpc-ws/shared/network/vpc-cidr` | Container VPC ID and CIDR |
| `/irpc-ws/shared/network/task-subnet-ids` | FileShare-01, FileShare-02, then SAP-01, SAP-02 (overflow), comma-separated |
| `/irpc-ws/shared/network/alb-subnet-ids` | WebApp-01, WebApp-02 |
| `/irpc-ws/shared/network/irpc-clients-prefix-list-id` | Managed prefix list of the IRPC office and VPN ranges |
| `/irpc-ws/shared/network/db-subnet-cidrs` | DB subnet CIDRs (SQL Server, port 1433) |
| `/irpc-ws/shared/alb-certificate-arn` | The certificate above |
| `/irpc-ws/shared/cognito/user-pool-id`, `/irpc-ws/shared/cognito/issuer`, `/irpc-ws/shared/cognito/domain` | The Cognito values above |
| `/irpc-ws/<usecase>/cognito/client-id` | Your team's app client ID |
| `/irpc-ws/<usecase>/bedrock/<model>` | The model value to call (C8) |
| `/irpc-ws/shared/bedrock/profiles-required`, `/irpc-ws/shared/bedrock/unprofiled-models` | Bedrock enforcement (C8) |

Parameters under `/irpc-ws/shared/`, `/irpc-ws/<usecase>/bedrock/` and `/irpc-ws/<usecase>/cognito/` are admin-owned and read-only. Put your own parameters under `/irpc-ws/<usecase>/app/`, but not under an `app/bedrock/` or `app/cognito/` segment: the admin guard matches `/irpc-ws/<usecase>/bedrock/*` and `/irpc-ws/<usecase>/cognito/*` across every depth, so a parameter such as `/irpc-ws/rcm/app/bedrock/prompt` is read-only too. Use another name, for example `/irpc-ws/<usecase>/app/model-prompt`.

### C2. Reachability and names

- Users reach an app only through its internal ALB in the WebApp subnets, with listeners on 443 (HTTPS) and 80 (redirect to 443). The IRPC firewall passes only web traffic (ssl/web-browsing) from the IRPC network and VPN to the WebApp subnets `10.24.136.0/24`.
- Any other listener port, or an ALB or task placed in other subnets, is unreachable from IRPC/VPN even though AWS accepts it. Never "fix" this by opening security groups wider or by using other ports.
- Open the app by its ALB DNS name (`internal-<name>-<id>.ap-southeast-7.elb.amazonaws.com`), which resolves to private IPs from the IRPC network and VPN. The browser warns about the self-signed certificate; that is expected.
- There is no private hosted zone and no custom domain during the workshop. Cognito callback URLs use the ALB DNS name. A friendly name (for example `app.irpc...`) needs IRPC IT and comes with the later migration; do not create Route 53 zones or records.

### C3. Deploy pattern

- One CDK (TypeScript) or CloudFormation stack per team app, named `irpc-ws-<usecase>-...` with the three required tags as stack tags (Part A section 4).
- Security groups: `irpc-ws-<usecase>-alb` allows 443 and 80 from the prefix list `irpc-clients-prefix-list-id` and the VPC CIDR; `irpc-ws-<usecase>-app` allows only the container port from the ALB security group. Default all-outbound egress is fine (internet via the IRPC firewall, 1433 to the DB subnets).
- ECS cluster named `irpc-ws-<usecase>-...` with Container Insights off (an ECS cluster not named `irpc-ws-*`, including `default`, is denied). Fargate service with `propagateTags: SERVICE`, no public IP, in the first two `task-subnet-ids` (FileShare; the SAP subnets only as overflow). Start with 0.25 vCPU / 0.5 GiB and `desiredCount: 1`; build images for `linux/amd64`.
- Internal ALB in both `alb-subnet-ids`: HTTPS 443 with the shared certificate and `ELBSecurityPolicy-TLS13-1-2-2021-06` (CDK `SslPolicy.RECOMMENDED_TLS`), HTTP 80 redirecting to 443, a health-check path. ALB and target group names are at most 32 characters.
- Logs: create the log group `/irpc-ws/<usecase>/<service>` in your stack with 3 months retention (`RetentionDays.THREE_MONTHS`).
- Execution role: import the shared `irpc-ws-ecs-task-execution` unchanged (it pulls from ECR `irpc-ws-*`, writes logs to `/irpc-ws/*` and reads secrets `irpc-ws-*`). Task role: let the stack create it; the boundary aspect below adds `irpc-ws-boundary` (Part A section 6).
- CDK: no `cdk bootstrap` (it needs role assumption, which is denied). Use `CliCredentialsStackSynthesizer`, which deploys with your own SSO credentials and puts assets in the admin bucket `irpc-ws-cdk-assets-337909773380-ap-southeast-7` and the ECR repository `irpc-ws-cdk-container-assets`. Deploy with `npx cdk deploy --profile irpc-ws` (Node.js 20 or later; for certificate errors see Part A section 1).

```typescript
import { App, CliCredentialsStackSynthesizer, Stack, Tags } from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as ecr from 'aws-cdk-lib/aws-ecr';
import * as ecs from 'aws-cdk-lib/aws-ecs';
import * as elbv2 from 'aws-cdk-lib/aws-elasticloadbalancingv2';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as sm from 'aws-cdk-lib/aws-secretsmanager';
import * as ssm from 'aws-cdk-lib/aws-ssm';

const uc = 'rcm', owner = 'somchai-j', name = `irpc-ws-${uc}-web-${owner}`; // your UseCase and owner
const app = new App();
const stack = new Stack(app, name, {
  env: { account: '337909773380', region: 'ap-southeast-7' },
  synthesizer: new CliCredentialsStackSynthesizer({
    fileAssetsBucketName: 'irpc-ws-cdk-assets-${AWS::AccountId}-${AWS::Region}',
    imageAssetsRepositoryName: 'irpc-ws-cdk-container-assets', bucketPrefix: `${name}/`, dockerTagPrefix: `${name}-`,
  }),
});
iam.PermissionsBoundary.of(stack).apply(iam.ManagedPolicy.fromManagedPolicyName(stack, 'Boundary', 'irpc-ws-boundary'));
Object.entries({ Environment: 'workshop', Owner: 'somchai.j@irpc.co.th', UseCase: uc }).forEach(([k, v]) => Tags.of(app).add(k, v));
// Synth-time lookups return "dummy-value-for-..." on the first pass; the fallback keeps that pass valid.
const look = (p: string, d: string) => {
  const v = ssm.StringParameter.valueFromLookup(stack, `/irpc-ws/shared/network/${p}`);
  return v.startsWith('dummy-value') ? d : v;
};
const vpcCidr = look('vpc-cidr', '10.0.0.0/16'), clients = look('irpc-clients-prefix-list-id', 'pl-00000000');
const vpc = ec2.Vpc.fromVpcAttributes(stack, 'Vpc', { vpcId: look('vpc-id', 'vpc-00000000'), vpcCidrBlock: vpcCidr, availabilityZones: ['ap-southeast-7a', 'ap-southeast-7b'] });
const two = (p: string) => ({ subnets: look(p, 'subnet-x,subnet-y').split(',').slice(0, 2).map((s, i) => ec2.Subnet.fromSubnetId(stack, `${p}${i}`, s)) });
const albSg = new ec2.SecurityGroup(stack, 'AlbSg', { vpc, securityGroupName: `${name}-alb` });
for (const port of [443, 80]) for (const peer of [ec2.Peer.prefixList(clients), ec2.Peer.ipv4(vpcCidr)]) albSg.addIngressRule(peer, ec2.Port.tcp(port));
const cluster = new ecs.Cluster(stack, 'Cluster', { vpc, clusterName: name, containerInsightsV2: ecs.ContainerInsights.DISABLED });
const task = new ecs.FargateTaskDefinition(stack, 'Task', { cpu: 256, memoryLimitMiB: 512,
  executionRole: iam.Role.fromRoleName(stack, 'Exec', 'irpc-ws-ecs-task-execution', { mutable: false }) });
task.addToTaskRolePolicy(new iam.PolicyStatement({ actions: ['bedrock:InvokeModel', 'bedrock:InvokeModelWithResponseStream'],
  resources: ['arn:aws:bedrock:us-east-1:337909773380:application-inference-profile/*', 'arn:aws:bedrock:us-east-1::foundation-model/*'] }));
const db = sm.Secret.fromSecretNameV2(stack, 'Db', `irpc-ws-${uc}-db`);
task.addContainer('web', {
  image: ecs.ContainerImage.fromEcrRepository(ecr.Repository.fromRepositoryName(stack, 'Repo', `irpc-ws-${uc}-web`), 'v1'), // C4
  portMappings: [{ containerPort: 8080 }],
  logging: ecs.LogDrivers.awsLogs({ streamPrefix: 'web', logGroup: new logs.LogGroup(stack, 'Logs', { logGroupName: `/irpc-ws/${uc}/web`, retention: logs.RetentionDays.THREE_MONTHS }) }),
  environment: { Bedrock__Region: 'us-east-1', Bedrock__ModelFamily: 'nova-lite',
    Bedrock__ModelId: ssm.StringParameter.valueForStringParameter(stack, `/irpc-ws/${uc}/bedrock/nova-lite`) },
  secrets: Object.fromEntries(['host', 'port', 'dbname', 'username', 'password'].map((k) => [`DB_${k.toUpperCase()}`, ecs.Secret.fromSecretsManager(db, k)])),
});
const service = new ecs.FargateService(stack, 'Service', { cluster, taskDefinition: task, serviceName: `${name}-svc`, desiredCount: 1,
  vpcSubnets: two('task-subnet-ids'), assignPublicIp: false, propagateTags: ecs.PropagatedTagSource.SERVICE,
  securityGroups: [new ec2.SecurityGroup(stack, 'AppSg', { vpc, securityGroupName: `${name}-app` })] });
const alb = new elbv2.ApplicationLoadBalancer(stack, 'Alb', { vpc, internetFacing: false, securityGroup: albSg, vpcSubnets: two('alb-subnet-ids') });
alb.addRedirect({ sourcePort: 80, targetPort: 443, open: false });
alb.addListener('Https', { port: 443, open: false, sslPolicy: elbv2.SslPolicy.RECOMMENDED_TLS,
  certificates: [elbv2.ListenerCertificate.fromArn(ssm.StringParameter.valueForStringParameter(stack, '/irpc-ws/shared/alb-certificate-arn'))],
}).addTargets('Web', { port: 8080, targets: [service], healthCheck: { path: '/health' } });
```

Plain CloudFormation follows the same shape: `AWS::SSM::Parameter::Value<String>` parameters for the IDs, the same names, tags, roles with `PermissionsBoundary`.

### C4. Images

- Laptop Docker: create the ECR repository `irpc-ws-<usecase>-<service>` (three tags), then `aws ecr get-login-password --profile irpc-ws --region ap-southeast-7 | docker login --username AWS --password-stdin 337909773380.dkr.ecr.ap-southeast-7.amazonaws.com`, `docker build --platform linux/amd64 -t <repository URI>:<version> .` and `docker push <repository URI>:<version>`. Use a version tag, not only `latest`.
- CodeBuild without Docker on the laptop: zip the source with its `buildspec.yml` and Dockerfile into your bucket `irpc-ws-<usecase>-build-<owner>`; project `irpc-ws-<usecase>-<service>-build` with an S3 source, `BUILD_GENERAL1_SMALL`, image `aws/codebuild/amazonlinux-x86_64-standard:5.0`, privileged mode, no VPC, service role `irpc-ws-codebuild-shared` (it reads `irpc-ws-*-build-*` buckets, pushes to ECR `irpc-ws-*` and logs to `/aws/codebuild/irpc-ws-*`).
- A workload role (Lambda, EventBridge rule, state machine) that starts a build must pass `computeTypeOverride` `BUILD_GENERAL1_SMALL` or `BUILD_GENERAL1_MEDIUM` (the permissions boundary denies it otherwise), and batch builds are denied.
- Base images: prefer `public.ecr.aws` and `mcr.microsoft.com`.

### C5. ALB to Lambda (serverless teams)

- A VPC Lambda `irpc-ws-...` in the task subnets is a target group of type `lambda` behind the same internal ALB (path rule such as `/api/*`; Part A section 5).
- The target needs `lambda:AddPermission` for principal `elasticloadbalancing.amazonaws.com` (CDK `targets.LambdaTarget(fn)` adds it; in CloudFormation add an `AWS::Lambda::Permission`). Give the function a log group `/aws/lambda/<function name>` with 3 months retention.

### C6. Database

- Your database lives on the shared instance; the secret `irpc-ws-<usecase>-db` (Secrets Manager, `ap-southeast-7`) holds the JSON keys `host`, `port`, `dbname`, `username` and `password`. The database name is your UseCase in lowerCamelCase (`rcm`, `customerDataCollection`, `other1`).
- In ECS, inject the keys with the task definition `secrets` field (C3: `DB_HOST`, `DB_PORT`, `DB_DBNAME`, `DB_USERNAME`, `DB_PASSWORD`) and build the connection string at startup. This is not a plaintext environment variable (Part A section 9).
- Node.js / Prisma: `datasource db { provider = "sqlserver" url = env("DATABASE_URL") }` with `DATABASE_URL="sqlserver://HOST:1433;database=DB;user=USER;password=PWD;encrypt=true;trustServerCertificate=true"`.
- .NET / EF Core: `options.UseSqlServer("Server=HOST,1433;Database=DB;User Id=USER;Password=PWD;Encrypt=True;TrustServerCertificate=True")`.
- Migrations and seed data run as a one-off task from your task definition: `aws ecs run-task --cluster <cluster> --task-definition <family> --launch-type FARGATE --network-configuration "awsvpcConfiguration={subnets=[<task subnet>],securityGroups=[<app sg>],assignPublicIp=DISABLED}" --overrides '<command override>' --propagate-tags TASK_DEFINITION --profile irpc-ws --region ap-southeast-7`.
- Your login can open only your own database; every other database is denied. Use `nvarchar` columns for Thai text.
- From your laptop on the IRPC network or VPN, SSMS or DBeaver reach the instance on port 1433 (`host` from the secret, read with `aws secretsmanager get-secret-value --secret-id irpc-ws-<usecase>-db --profile irpc-ws`). Never paste the password into chat or prompts.

### C7. Authentication

- Authority and client ID come from configuration: the authority above and `/irpc-ws/<usecase>/cognito/client-id`. The client is public (authorization code + PKCE, no client secret), scopes `openid email profile`; users sign in with their IRPC IAM Identity Center account.
- Add your callback and sign-out URLs on the ALB DNS name (for ASP.NET `https://<ALB DNS name>/signin-oidc` and `https://<ALB DNS name>/signout-callback-oidc`). `update-user-pool-client` resets every field you leave out, so describe first and pass every field back:

```bash
aws cognito-idp describe-user-pool-client --user-pool-id ap-southeast-7_nPA2oYWMe --client-id <client id> --profile irpc-ws --region ap-southeast-7
aws cognito-idp update-user-pool-client --user-pool-id ap-southeast-7_nPA2oYWMe --client-id <client id> --profile irpc-ws --region ap-southeast-7 \
  --allowed-o-auth-flows code --allowed-o-auth-flows-user-pool-client --allowed-o-auth-scopes openid email profile \
  --supported-identity-providers <SupportedIdentityProviders from describe> \
  --callback-urls <existing CallbackURLs> https://<ALB DNS name>/signin-oidc \
  --logout-urls <existing LogoutURLs> https://<ALB DNS name>/signout-callback-oidc
```

- Also pass back any other field the describe output shows (for example `ExplicitAuthFlows` or token validity).
- Change only your own team's client. Editing another team's client is a workshop violation and raises an alert to the facilitators.
- Behind the ALB the app sees HTTP: in ASP.NET call `UseForwardedHeaders` (X-Forwarded-Proto) before `UseAuthentication`; in Node.js trust the ALB's `X-Forwarded-Proto` header, so redirect URIs use `https`.

### C8. Bedrock on AWS

- Read only your own UseCase's `/irpc-ws/<your usecase>/bedrock/*` parameters. Each one is your team's own application inference profile, and Bedrock cost is reported per UseCase from the profile's `UseCase` tag; invoking another team's profile ARN books your spend to that team and skews the report, so never hardcode or copy a profile ARN you saw elsewhere.
- Set `Bedrock__ModelId` from SSM `/irpc-ws/<usecase>/bedrock/<model>` (CDK `ssm.StringParameter.valueForStringParameter` into the container environment, C3) and pass it unchanged as `modelId` (Part A section 7). The value is your team's application inference profile ARN, or the plain model ID for a model without a profile.
- Set `Bedrock__ModelFamily` to the same short name: `nova-lite`, `nova-micro`, `nova-pro`, `gpt-oss-120b`, `gpt-oss-20b`, `titan-embed-v2` or `nova-mm-embed`. Use it for request-body choices (for example the embeddings body), never the model ID text.
- gpt-oss on AWS works as in Part A section 7: `maxTokens` about 2000, read the `text` block, not `reasoningContent`.
- The task or Lambda role needs `bedrock:InvokeModel` and `bedrock:InvokeModelWithResponseStream` on `arn:aws:bedrock:us-east-1:337909773380:application-inference-profile/*` and `arn:aws:bedrock:us-east-1::foundation-model/*` (C3).
- When `/irpc-ws/shared/bedrock/profiles-required` is `true`, a call with a plain model ID is denied (laptop and AWS), except for the models listed in `/irpc-ws/shared/bedrock/unprofiled-models` (`none` when every model has a profile). The SSM value always works, so on your laptop set `Bedrock__ModelId` to it as well: `aws ssm get-parameter --name /irpc-ws/<usecase>/bedrock/nova-lite --query Parameter.Value --output text --profile irpc-ws`.

### C9. Mock adapters

- Deploy with the mock implementation of every external-system adapter (Part A section 3), chosen by configuration. Load the mock seed data into your team database (C6) with the migration task.

### C10. Cost and lifecycle

- After work and between workshop days: `aws ecs update-service --cluster <cluster> --service <service> --desired-count 0 --profile irpc-ws --region ap-southeast-7`, and stop one-off tasks (Part A section 11).
- At the account budget limit the admin budget-stop scales every service to 0 and stops standalone tasks; the admin restores them after a budget decision. Do not remove the `irpc-ws-autostop-desired` tag it sets.
- `Migrate` tags follow Part A section 4. For a Migrate app, keep the IaC, Dockerfile and image in your repository and ECR.
