# Portcullis CLI as a container — the secondary distribution channel.
#
# The primary channels are NuGet: Portcullis.Analyzers (rules as build diagnostics) and the
# `portcullis` dotnet tool. This image exists for the case those two do not serve: a CI
# system that is not GitHub Actions and would rather not install a .NET SDK at all
# (Jenkins, GitLab CI, Woodpecker, a plain `docker run` in a Makefile). See
# docs/DISTRIBUTION.md for why it is NOT the default, and specifically not the
# implementation of the GitHub Action.
#
# Multi-stage, non-root, no SDK in the final image — architecture-standards P6.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore against the project files alone first, so the (slow) restore layer is cached
# and only re-runs when a project file actually changes.
COPY Directory.Build.props ./
COPY portcullis.sln ./
COPY src/Portcullis.Rules/Portcullis.Rules.csproj src/Portcullis.Rules/
COPY src/Portcullis.Engine/Portcullis.Engine.csproj src/Portcullis.Engine/
COPY src/Portcullis.Cli/Portcullis.Cli.csproj src/Portcullis.Cli/
COPY src/Portcullis.CiComment/Portcullis.CiComment.csproj src/Portcullis.CiComment/
RUN dotnet restore src/Portcullis.Cli/Portcullis.Cli.csproj \
 && dotnet restore src/Portcullis.CiComment/Portcullis.CiComment.csproj

COPY src/ src/
ARG VERSION=0.1.0
RUN dotnet publish src/Portcullis.Cli/Portcullis.Cli.csproj \
        -c Release -o /app --no-restore -p:Version=$VERSION \
 && dotnet publish src/Portcullis.CiComment/Portcullis.CiComment.csproj \
        -c Release -o /app --no-restore -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

# git is a real runtime dependency, not a convenience: --provenance-range shells out to
# `git diff`/`git blame` to resolve which lines a commit range changed and who authored
# them. Without it the provenance signal degrades to empty and the diff-scoped gate
# silently has nothing to scope to.
RUN apt-get update \
 && apt-get install --no-install-recommends -y git \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# The scanned repository is mounted here; git needs to trust a directory it does not own.
RUN git config --system --add safe.directory '*'

RUN useradd --create-home --uid 1001 portcullis
USER portcullis
WORKDIR /workspace

ENTRYPOINT ["dotnet", "/app/Portcullis.Cli.dll"]
CMD ["scan", "/workspace"]

LABEL org.opencontainers.image.title="Portcullis" \
      org.opencontainers.image.description="Fail a build when C# code violates your team's declared architecture." \
      org.opencontainers.image.source="https://github.com/konradcinkusz/letsgolegacy.portcullis" \
      org.opencontainers.image.licenses="MIT"
