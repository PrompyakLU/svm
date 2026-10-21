# SVM — Security Vulnerability Management

แพลตฟอร์มรวมศูนย์การบริหารจัดการช่องโหว่ด้านความมั่นคงปลอดภัย (ประเมินความเสี่ยง, workflow, SLA, verification) — core end-to-end slice

## Stack
- **Web**: Angular 22 (standalone, strict TS) + Tailwind 4 + daisyUI 5
- **API**: ASP.NET Core .NET 10 (C# 14) — Layered/Clean-ish (Api / Application / Domain / Infrastructure)
- **Data**: SQL Server + EF Core 10
- **Auth**: local JWT หลัง `IAuthProvider` (OIDC-ready)
- External systems ทั้งหมดเป็น mock adapter + seed (`Integration:UseMock=true`)

## โครงสร้าง repo
```
svm/
├─ web/      # Angular 22 SPA
├─ api/      # .NET 10 solution (SvmApi.slnx)
└─ docs/adr/ # Architecture Decision Records
```

## Prerequisites
- .NET SDK 10, Node.js 24 LTS, SQL Server (local หรือ docker), Git

## เริ่มใช้งาน (dev)
```bash
# SQL Server (Docker Desktop must be running; .env supplies the local SA password)
docker-compose -f docker-compose.dev.yml up -d

# API (configure ConnectionStrings:Svm, Auth:Jwt:SigningKey and Seed:DemoPassword
# through user-secrets/environment before starting; never commit their values)
cd api
dotnet restore
dotnet ef database update -p src/SvmApi.Infrastructure -s src/SvmApi.Api -- --environment Development
dotnet run --project src/SvmApi.Api --launch-profile http  # http://localhost:5181; /openapi/v1.json

# Web
cd ../web
npm ci
npm start                                   # http://localhost:4200
```

The default web profile calls the real API through `web/proxy.dev.json`. Use `npm run start:demo` for the explicit browser demo. Real accounts use the local seed password and the permissions/scopes defined in `SeedData.Access.cs`; `admin` manages configuration, while `officer1` exercises the vulnerability flow.

```powershell
dotnet test api/SvmApi.slnx --no-restore
npm --prefix web test -- --watch=false
npm --prefix web run build
powershell -NoProfile -ExecutionPolicy Bypass -File api/smoke-wave4.ps1 -BaseUrl http://127.0.0.1:5181
```

## Spec & Design
เอกสาร spec อยู่ที่ `.aidlc/specs/svm/` (requirements, design, tasks) — พัฒนาด้วย AI-DLC workflow. SVM uses the approved **light theme**. ทิศทาง UI ปัจจุบันอยู่ที่ [`.kiro/steering/design-system.md`](.kiro/steering/design-system.md), dashboard contract อยู่ที่ [`docs/ui/dashboard.md`](docs/ui/dashboard.md), และค่า token ที่ใช้งานจริงอยู่ใน `web/src/styles.css`.

## หมายเหตุความปลอดภัย
- ห้าม commit secret (ใช้ env / user-secrets / secret store)
- ไม่มีข้อมูลส่วนบุคคลจริงใน dev/test (PDPA) — ใช้ข้อมูลจำลองเท่านั้น
- ห้าม call external/SOAP API จริง — mock + log เท่านั้น
