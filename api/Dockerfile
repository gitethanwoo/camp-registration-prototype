FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Camp.Api/Camp.Api.csproj Camp.Api/
RUN dotnet restore Camp.Api/Camp.Api.csproj
COPY Camp.Api/ Camp.Api/
RUN dotnet publish Camp.Api/Camp.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Camp.Api.dll"]
