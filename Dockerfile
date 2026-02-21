FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/EditRelease/EditRelease.csproj", "EditRelease/"]
RUN dotnet restore EditRelease/EditRelease.csproj
COPY ["src/EditRelease", "EditRelease/"]
RUN dotnet publish EditRelease/EditRelease.csproj -c Release --no-restore -o /publish

# Label the container
LABEL maintainer="step-security"
LABEL repository="https://github.com/step-security/EditRelease"
LABEL homepage="https://github.com/step-security/EditRelease"

# Label as GitHub Action
LABEL com.github.actions.name="Edit Release"
LABEL com.github.actions.description="A GitHub Action for editing an existing release."
LABEL com.github.actions.icon="edit"
LABEL com.github.actions.color="purple"

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /publish .
ENV DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "/app/EditRelease.dll"]
