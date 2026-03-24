# homelab.mqtt-topics-translator

Simple service that receives messages from MQTT topics and republishes them under different topic paths. Useful for bridging, renaming, or fan-out of MQTT messages — for example, ensuring that turning on a pool redox system also activates the pump.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
- [Local Development](#local-development)
- [Running Tests](#running-tests)
- [Release Process](#release-process)
- [Manual Local Publishing](#manual-local-publishing)
- [GitHub Secrets Setup](#github-secrets-setup)
- [Docker Image Usage](#docker-image-usage)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://docs.docker.com/get-docker/) and Docker Compose
- (Optional) [dvm](https://github.com/howtowhale/dvm) for managing Docker versions locally

## Configuration

The service is configured via `appsettings.json` (or environment variable overrides):

| Setting | Description | Default |
|---------|-------------|---------|
| `MqttTranslatorSettings__MqttServerConfig__Server` | MQTT broker hostname | `localhost` |
| `MqttTranslatorSettings__MqttServerConfig__Port` | MQTT broker port | `1883` |
| `MqttTranslatorSettings__MqttServerConfig__ClientId` | Client ID for the MQTT connection | `MqttTranslator` |
| `MqttTranslatorSettings__MqttServerConfig__Username` | MQTT username | |
| `MqttTranslatorSettings__MqttServerConfig__Password` | MQTT password | |
| `MqttTranslatorSettings__MqttMappingConfigFile` | Path to the JSON mapping config file | `sample-mapping.json` |

The mapping config file defines source-to-destination topic translations. See `src/mqtttranslator/sample-mapping.json` for the format.

## Local Development

Build the .NET project:

```bash
dotnet build src/MszCool.MqttTopicsTranslator.sln
```

Run with Docker Compose (starts Mosquitto + the translator):

```bash
docker compose up -d --build
```

Stop services:

```bash
docker compose down
```

## Running Tests

**Via Docker Compose** (recommended — spins up Mosquitto, runs integration tests, tears down):

```bash
./scripts/docker-test.sh
```

**Directly with dotnet** (requires a running MQTT broker on localhost:1883):

```bash
dotnet test src/mqtttranslator.tests/MszCool.MqttTopicsTranslator.Tests.csproj \
    --logger 'console;verbosity=detailed'
```

## Release Process

This project uses a single `release` branch with GitHub Actions for automated testing, tagging, and Docker image publishing.

### Branch Strategy

```
feature-branch ──PR──> main ──PR──> release
                         │              │
                    dev/CI tests    tests + auto-tag + Docker publish
```

1. **Develop** on feature branches, merge to `main` via pull request.
2. **Release** by opening a pull request from `main` to `release`.

### PR Gate (Integration Tests)

When a pull request targets the `release` branch, the **Integration Tests** workflow runs automatically:

- Starts Docker Compose services (Mosquitto + translator)
- Runs the full integration test suite
- **If tests fail, the PR cannot be merged** (requires [branch protection](#branch-protection) configuration)

### Automated Release (on merge)

When a PR is merged to `release`, the **Release** workflow automatically:

1. Runs integration tests again (safety net)
2. Computes the next release tag in `yyyyMMdd.N` format:
   - `N` starts at `0` and increments for each release on the same day
   - Examples: `20260324.0`, `20260324.1`, `20260325.0`
3. Creates and pushes the git tag
4. Builds the Docker image
5. Pushes `<repo>:<tag>` and `<repo>:latest` to Docker Hub

### Branch Protection

To enforce the PR gate, configure branch protection on the `release` branch in GitHub:

1. Go to **Settings > Branches > Add branch protection rule**
2. Branch name pattern: `release`
3. Enable **Require status checks to pass before merging**
4. Select the **Integration Tests / integration-tests** status check
5. (Recommended) Enable **Require branches to be up to date before merging**

## Manual Local Publishing

The `scripts/docker-publish.sh` script can be used to build and push Docker images locally from the `release` branch.

**Usage:**

```bash
./scripts/docker-publish.sh [--force] [--tag TAG] <dockerhub-repo>
```

**Options:**

| Flag | Description |
|------|-------------|
| `--force` | Skip running integration tests before publishing |
| `--tag TAG` | Use an explicit tag instead of auto-detecting |

**Tag resolution order:**

1. If `--tag TAG` is provided, use that tag
2. If a git tag matching `yyyyMMdd.N` exists on HEAD, use that tag (typical after pulling a CI-tagged release)
3. Otherwise, compute the next tag from existing git tags for today and create it locally

**Examples:**

```bash
# Auto-detect tag from HEAD (pull the release branch first to get CI tags)
git checkout release && git pull
./scripts/docker-publish.sh mszcool/mqtt-topics-translator

# Skip tests and use auto-detection
./scripts/docker-publish.sh --force mszcool/mqtt-topics-translator

# Use an explicit tag
./scripts/docker-publish.sh --tag 20260324.5 mszcool/mqtt-topics-translator
```

## GitHub Secrets Setup

The Release workflow requires two repository secrets for Docker Hub authentication:

1. Go to **Settings > Secrets and variables > Actions**
2. Add the following repository secrets:

| Secret | Description |
|--------|-------------|
| `DOCKERHUB_USERNAME` | Your Docker Hub username |
| `DOCKERHUB_TOKEN` | A Docker Hub [access token](https://hub.docker.com/settings/security) (not your password) |

## Docker Image Usage

Pull and run the published image:

```bash
docker pull mszcool/mqtt-topics-translator:latest

docker run -d \
    -e MqttTranslatorSettings__MqttServerConfig__Server=your-broker \
    -e MqttTranslatorSettings__MqttServerConfig__Port=1883 \
    -e MqttTranslatorSettings__MqttServerConfig__ClientId=MqttTranslator \
    -e MqttTranslatorSettings__MqttServerConfig__Username=user \
    -e MqttTranslatorSettings__MqttServerConfig__Password=pass \
    -e MqttTranslatorSettings__MqttMappingConfigFile=/app/mapping/mapping.json \
    -v /path/to/your/mapping.json:/app/mapping/mapping.json:ro \
    mszcool/mqtt-topics-translator:latest
```

Or use a specific version:

```bash
docker pull mszcool/mqtt-topics-translator:20260324.0
```

## License

See [LICENSE](LICENSE) for details.
