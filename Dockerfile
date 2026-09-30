# Stage 1: build (pakai SDK, ukurannya besar tapi hanya dipakai saat build)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj dulu supaya layer restore ter-cache
COPY *.csproj ./
RUN dotnet restore

# Copy sisa kode lalu publish
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: runtime (lebih kecil, tanpa SDK)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Jalankan sebagai non-root
USER app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ApiBe.dll"]