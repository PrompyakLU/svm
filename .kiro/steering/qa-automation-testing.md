---
inclusion: always
---
# QA Automation-Testing — Common Defaults (Workshop)

เอกสารนี้เป็น steering กลางสำหรับ skill ชุด `automation-testing-*` ให้ใช้ค่า default
เดียวกันได้กับทุก project เพื่อให้รอบ QA automation-testing **จบภายใน 1 ชั่วโมง**
และข้ามคำถาม interactive ที่ตอบไว้แล้วด้านล่าง (ถามเฉพาะสิ่งที่ steering นี้ไม่ได้กำหนด).

> ขอบเขตการใช้งาน: **Workshop เท่านั้น** — ค่าเหล่านี้ตั้งใจให้รันเร็ว/ครอบคลุม
> ไม่ใช่สำหรับ production QA gate.

## Stage 1/7 — Intake

- **Scope: full** — ครอบคลุมทั้ง 3 ชั้น: **UI + API + Unit/Integration**
  (ไม่ต้องถามเลือก smoke/focused/full — ใช้ full เสมอ).
- จัดชุด test ตาม **user journey** หลักของแอปแบบ end-to-end
  (ไล่ตาม flow ที่ผู้ใช้งานจริงทำ ไม่ใช่ไล่ทีละ endpoint/หน้าแยกกัน).
- ขอบเขตการใช้งาน: **Workshop เท่านั้น** — **คิด test case ไม่เยอะ**
  เน้นเฉพาะ user journey หลัก/happy path + negative case สำคัญไม่กี่อัน
  เพื่อให้ทั้งรอบเสร็จภายในประมาณ 1 ชั่วโมง (อย่าแตก case ละเอียดเกินจำเป็น).
- ให้ scan workspace หา default (target app, route, API spec, test runner)
  เองก่อน แล้วถามผู้ใช้เฉพาะข้อมูลที่หาไม่ได้จริง ๆ (เช่น credential / live URL).

## Stage 5/7 — Scale Suite

- **UI responsive device: desktop เท่านั้น — 1440×900** (ไม่ต้องถามเลือก device,
  ไม่ต้องรัน tablet/mobile ในรอบ workshop นี้).
- รัน test case เรียงตาม TC-id (`workers: 1`) จนครบ.
- auto-fix script ที่ flaky ได้สูงสุด 3 ครั้งต่อ case ตามปกติของ skill.
- เก็บ evidence: screenshot ต่อ case + วิดีโอ end-to-end 1 ไฟล์ (desktop).

## สิ่งที่ยังต้องถามผู้ใช้ (ไม่ได้ fix ไว้)

- target เป็น app ใน repo นี้ หรือ live URL + credential/access.
- source of truth เพิ่มเติม (requirement / API spec / existing test cases) ถ้ามี.
- report metadata สำหรับหน้าปก (ถ้าต้องการ).
