---
inclusion: manual
---

# SRS Document Style — Metro Systems Corporation Plc.

ข้อกำหนดด้านรูปแบบและ typography สำหรับการจัดทำเอกสาร Software Requirements Specification (SRS) ใช้คู่กับแม่แบบโครงสร้างเอกสารใน #[[file:srs-template.md]] — `srs-template.md` คุม **โครงสร้าง/หัวข้อ** ของเอกสาร ส่วนไฟล์นี้คุม **รูปแบบการนำเสนอ** (ฟอนต์ ขนาด สไตล์ การจัดวาง)

> ใช้เมื่อจัดทำ/ส่งออกเอกสาร SRS เป็นไฟล์เอกสาร (เช่น `.docx` / PDF) เพื่อให้รูปลักษณ์สม่ำเสมอทุกฉบับ

## Font Family (ฟอนต์หลัก)

- **ฟอนต์หลักของเอกสาร**: **TH Sarabun New** (รองรับทั้งภาษาไทยและอังกฤษ ใช้ฟอนต์เดียวกันทั้งเอกสารเพื่อความสม่ำเสมอ)
- ค่าตัวแปรที่อ้างถึงในตาราง:
  - `thaiFontName` = `TH Sarabun New`
  - `engFontName` = `TH Sarabun New`
  - `thaiFontSize` = `16`
  - `engFontSize` = `16`

## Typography Specification (ข้อกำหนดตัวอักษร)

| Element | Font | Size | Style |
|---------|------|------|-------|
| Heading 1 | TH Sarabun New | 18pt | Bold |
| Heading 2 | TH Sarabun New | 16pt | Bold |
| Heading 3 | TH Sarabun New | 14pt | Bold |
| เนื้อหาไทย | TH Sarabun New | 16pt | Normal |
| เนื้อหาอังกฤษ | TH Sarabun New | 16pt | Normal |
| ตาราง Header | mixed (TH Sarabun New) | 12pt | Bold |
| ตาราง Body | mixed (TH Sarabun New) | 11pt | Normal |
| Header/Footer | TH Sarabun New | 9pt | Normal |

> "mixed" = ใช้ TH Sarabun New เป็นหลักสำหรับทั้งข้อความไทยและอังกฤษในเซลล์ตาราง

## Layout & Formatting (การจัดวางและรูปแบบ)

- **ขนาดกระดาษ**: A4 (แนวตั้ง portrait เป็นค่าเริ่มต้น)
- **ระยะขอบ (margin)**: บน/ล่าง/ซ้าย/ขวา ประมาณ 2.5 ซม. (ปรับตามมาตรฐานองค์กรหากกำหนดไว้)
- **ระยะห่างบรรทัด (line spacing)**: 1.0–1.15 สำหรับเนื้อหา เว้นช่องว่างระหว่างย่อหน้าให้อ่านง่าย
- **การจัดข้อความ**: ชิดซ้าย (left-aligned) เป็นค่าเริ่มต้น หัวข้อหลักจัดตามลำดับชั้น
- **เลขหัวข้อ**: ใช้เลขลำดับชั้นสอดคล้องกับ `srs-template.md` (เช่น 7, 7.1, 7.3) ไม่ใช้อิโมจิประกอบหัวข้อ
- **ตาราง**: มีเส้นขอบชัดเจน, แถว header ทำตัวหนา (ตาม spec ด้านบน), ข้อความในเซลล์ไม่ชนขอบ

## Header / Footer (หัว-ท้ายกระดาษ)

- **Header**: ชื่อระบบ/ชื่อเอกสาร และ/หรือ โลโก้ Metro Systems Corporation Plc. (ใช้โลโก้ที่ได้รับอนุญาตเท่านั้น)
- **Footer**: เลขหน้า (เช่น `Page X of Y`), รหัส/เวอร์ชันเอกสาร, ระดับการจัดชั้นความลับ (Classification)
- ฟอนต์ Header/Footer: TH Sarabun New 9pt Normal

## Cover Page Emphasis (หน้าปก)

- ชื่อเอกสาร "Software Requirements Specification (SRS)" และชื่อระบบ แสดงเด่นกลางหน้า (ขนาดใหญ่กว่าปกติได้ตามความเหมาะสม เช่น 22–28pt Bold)
- แสดงข้อมูลตามหัวข้อ Cover Page ใน `srs-template.md` (รหัสเอกสาร, เวอร์ชัน, วันที่, ผู้จัดทำ, หน่วยงาน, Classification)

## หลักการ (Principles)

- รูปลักษณ์ต้องสุภาพ อ่านง่าย เป็นทางการ และ **สม่ำเสมอทั้งเอกสาร** — ยึดตารางข้อกำหนดด้านบนทุกฉบับ
- หากองค์กรมี template/มาตรฐานเอกสารทางการที่กำหนดค่าอื่นไว้ ให้ยึดมาตรฐานองค์กรก่อน แล้วบันทึกความต่างไว้
- **ห้ามเดา** ค่าที่ไม่ได้กำหนด — หากต้องใช้ค่าที่ไม่มีระบุไว้ ให้ยืนยันกับผู้จัดทำ/เจ้าของเอกสารก่อน
