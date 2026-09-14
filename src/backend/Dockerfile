FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Rfid.sln ./
COPY Rfid.Domain/Rfid.Domain.csproj Rfid.Domain/
COPY Rfid.Application/Rfid.Application.csproj Rfid.Application/
COPY Rfid.Infrastructure/Rfid.Infrastructure.csproj Rfid.Infrastructure/
COPY Rfid.Api/Rfid.Api.csproj Rfid.Api/
COPY Rfid.Tests/Rfid.Tests.csproj Rfid.Tests/
RUN dotnet restore Rfid.Api/Rfid.Api.csproj
COPY . .
RUN dotnet publish Rfid.Api/Rfid.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rfid.Api.dll"]
