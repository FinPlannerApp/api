# --- build stage (.NET 10) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# publish ONLY the API project
RUN dotnet restore ./src/Presentation/API.csproj
RUN dotnet publish ./src/Presentation/API.csproj -c Release -o /app /p:UseAppHost=false

# --- runtime stage (.NET 10 ASP.NET) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The non-root user must be able to write logs and uploads (Serilog file sink, issue attachments, receipts).
RUN mkdir -p /app/logs /app/wwwroot/uploads && chown -R $APP_UID /app/logs /app/wwwroot/uploads
USER $APP_UID
ENV PORT=8080
# Render provides $PORT; bind Kestrel to it via a tiny entrypoint
CMD ["sh","-c","ASPNETCORE_URLS=http://0.0.0.0:${PORT} dotnet API.dll"]
