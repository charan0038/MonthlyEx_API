# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["MonthExpenses.Api.csproj", "./"]
RUN dotnet restore "MonthExpenses.Api.csproj"

# Copy everything else and publish
COPY . .
RUN dotnet publish "MonthExpenses.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MonthExpenses.Api.dll"]
