FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/JIssWeb.sln ./
COPY backend/src/JIssWeb.Common/JIssWeb.Common.csproj JIssWeb.Common/
COPY backend/src/JIssWeb.Application/JIssWeb.Application.csproj JIssWeb.Application/
COPY backend/src/JIssWeb.Domain/JIssWeb.Domain.csproj JIssWeb.Domain/
COPY backend/src/JIssWeb.Infrastructure/JIssWeb.Infrastructure.csproj JIssWeb.Infrastructure/
COPY backend/src/JIssWeb.Customer.Api/JIssWeb.Customer.Api.csproj JIssWeb.Customer.Api/
RUN dotnet restore JIssWeb.Customer.Api/JIssWeb.Customer.Api.csproj
COPY backend/src/JIssWeb.Common/ JIssWeb.Common/
COPY backend/src/JIssWeb.Application/ JIssWeb.Application/
COPY backend/src/JIssWeb.Domain/ JIssWeb.Domain/
COPY backend/src/JIssWeb.Infrastructure/ JIssWeb.Infrastructure/
COPY backend/src/JIssWeb.Customer.Api/ JIssWeb.Customer.Api/
RUN dotnet publish JIssWeb.Customer.Api/JIssWeb.Customer.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5098
ENV ASPNETCORE_URLS=http://+:5098
ENTRYPOINT ["dotnet", "JIssWeb.Customer.Api.dll"]
