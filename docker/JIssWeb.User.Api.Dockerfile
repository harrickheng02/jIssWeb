FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/JIssWeb.sln ./
COPY backend/src/JIssWeb.Common/JIssWeb.Common.csproj JIssWeb.Common/
COPY backend/src/JIssWeb.Application/JIssWeb.Application.csproj JIssWeb.Application/
COPY backend/src/JIssWeb.Domain/JIssWeb.Domain.csproj JIssWeb.Domain/
COPY backend/src/JIssWeb.Infrastructure/JIssWeb.Infrastructure.csproj JIssWeb.Infrastructure/
COPY backend/src/JIssWeb.User.Api/JIssWeb.User.Api.csproj JIssWeb.User.Api/
RUN dotnet restore JIssWeb.User.Api/JIssWeb.User.Api.csproj
COPY backend/src/JIssWeb.Common/ JIssWeb.Common/
COPY backend/src/JIssWeb.Application/ JIssWeb.Application/
COPY backend/src/JIssWeb.Domain/ JIssWeb.Domain/
COPY backend/src/JIssWeb.Infrastructure/ JIssWeb.Infrastructure/
COPY backend/src/JIssWeb.User.Api/ JIssWeb.User.Api/
RUN dotnet publish JIssWeb.User.Api/JIssWeb.User.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5097
ENV ASPNETCORE_URLS=http://+:5097
ENTRYPOINT ["dotnet", "JIssWeb.User.Api.dll"]
