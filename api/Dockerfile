# SVM API — multi-stage build (.NET 10). linux/amd64 for AWS Fargate.
# ยังไม่ deploy จริง — ใช้รัน local ด้วย docker compose / ทดสอบ image ได้
# base images จาก mcr.microsoft.com ตาม workshop policy (C4)

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# คัดลอกไฟล์ที่กำหนด version ก่อน เพื่อ cache layer ของ restore
# หมายเหตุ: ไม่คัดลอก nuget.config ของ repo (มันตั้ง globalPackagesFolder เป็น path เฉพาะเครื่อง
# บน drive D: ซึ่งไม่มีใน container) — ให้ container restore จาก nuget.org ด้วยค่า default
COPY global.json Directory.Packages.props ./
COPY SvmApi.slnx ./
COPY src/ ./src/

# restore + publish (locked restore ตาม packages.lock.json)
RUN dotnet restore src/SvmApi.Api/SvmApi.Api.csproj
RUN dotnet publish src/SvmApi.Api/SvmApi.Api.csproj \
    -c Release -o /app/publish \
    --no-restore /p:UseAppHost=false

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# รันด้วย non-root user (secure by default)
RUN useradd --uid 1001 --create-home --shell /usr/sbin/nologin svm
USER 1001

COPY --from=build /app/publish .

# Kestrel ฟังที่ 8080 ภายใน container (ตรงกับ compose / Fargate container port)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "SvmApi.Api.dll"]
