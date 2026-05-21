FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6 AS build
WORKDIR /src
COPY ["src/EditRelease/EditRelease.csproj", "EditRelease/"]
RUN dotnet restore EditRelease/EditRelease.csproj
COPY ["src/EditRelease", "EditRelease/"]
RUN dotnet publish EditRelease/EditRelease.csproj -c Release --no-restore -o /publish

# Label the container
LABEL maintainer="step-security"
LABEL repository="https://github.com/step-security/edit-release"
LABEL homepage="https://github.com/step-security/edit-release"

# Label as GitHub Action
LABEL com.github.actions.name="Edit Release"
LABEL com.github.actions.description="A GitHub Action for editing an existing release."
LABEL com.github.actions.icon="edit"
LABEL com.github.actions.color="purple"

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:43d31267de3f39d00661db8054f882041933c4ef894c0a0a1c54f9704dc0eadc AS final
WORKDIR /app
COPY --from=build /publish .
ENV DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "/app/EditRelease.dll"]
