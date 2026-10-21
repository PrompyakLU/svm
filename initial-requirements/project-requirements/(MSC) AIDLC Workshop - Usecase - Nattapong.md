| AIDLC Workshop Training Assessment by MSC |  |  |  |  |
| --- | --- | --- | --- | --- |
| APP-01 MLArena |  |  |  |  |
| INTRODUCTION APP / USECASE |  |  |  |  |
| App ID: | APP-01 |  |  |  |
| App / Usecase Name: | SVM (Security Vulnerability Management) |  |  |  |
| Usecase Type:<br>(Greenfield/Brownfield) | Greenfield |  |  |  |
| Usecase Type<br>NOTE: | Source Code |  |  |  |
| Related Systems: | - |  |  |  |
| Systems Scope: | In scope: ระบบ Security Vulnerability Management System เป็นแพลตฟอร์มกลางขององค์กรสำหรับบริหารจัดการช่องโหว่ด้านความมั่นคงปลอดภัยสารสนเทศแบบครบวงจร โดยรองรับการรวบรวมข้อมูลช่องโหว่จากเครื่องมือและแหล่งข้อมูลหลายประเภท เช่น Infrastructure Vulnerability Scanner, Web Application Scanner, Static Application Security Testing (SAST), Dynamic Application Security Testing (DAST), Software Composition Analysis (SCA), Cloud Security Scanner, Container Security Scanner, Penetration Testing, Manual Security Assessment และ Threat Intelligence<br><br>ระบบต้องสามารถนำเข้าข้อมูลผ่าน REST API, File Import และการบันทึกข้อมูลโดยผู้ใช้งาน พร้อมตรวจสอบความถูกต้อง ปรับรูปแบบข้อมูลให้เป็นมาตรฐาน และตรวจหารายการซ้ำก่อนจัดเก็บ โดยเชื่อมโยงรายการช่องโหว่กับข้อมูลทรัพย์สินสารสนเทศ เจ้าของระบบ หน่วยงานรับผิดชอบ ระดับความสำคัญของทรัพย์สิน และข้อมูลอ้างอิงด้านความปลอดภัย เช่น Common Vulnerabilities and Exposures (CVE), Common Weakness Enumeration (CWE), Common Vulnerability Scoring System (CVSS) และ Known Exploited Vulnerabilities (KEV)<br><br>ระบบต้องสนับสนุนการประเมินและจัดลำดับความเสี่ยงของช่องโหว่ โดยพิจารณาปัจจัยด้านเทคนิคและด้านธุรกิจร่วมกัน เช่น CVSS Score, Asset Criticality, Business Impact, Internet Exposure, Data Classification, Exploit Availability, Threat Intelligence, Existing Security Controls, Vulnerability Age และ Regulatory Impact เพื่อคำนวณระดับความเสี่ยงและกำหนดกรอบระยะเวลาการแก้ไขหรือ Service Level Agreement (SLA) ที่เหมาะสม<br><br>ระบบต้องรองรับกระบวนการบริหารช่องโหว่ตั้งแต่การตรวจพบ การตรวจสอบยืนยัน การจัดประเภท การมอบหมายผู้รับผิดชอบ การสร้างงานแก้ไข การติดตามสถานะ การแนบหลักฐาน การแจ้งผลการแก้ไข การตรวจสอบซ้ำ การอนุมัติปิดรายการ และการเปิดรายการกลับมาใหม่ในกรณีที่ช่องโหว่ยังคงปรากฏหรือเกิดขึ้นซ้ำ<br><br>ระบบต้องรองรับกระบวนการ Risk Acceptance, False Positive, Compensating Control, SLA Extension และ Security Exception โดยกำหนดลำดับการอนุมัติตามระดับความเสี่ยง ระดับความสำคัญของทรัพย์สิน หน่วยงานเจ้าของระบบ และ Approval Matrix ขององค์กร พร้อมกำหนดวันหมดอายุ การแจ้งเตือนก่อนหมดอายุ และการทบทวนรายการตามรอบระยะเวลาที่กำหนด<br><br>ระบบต้องสามารถเชื่อมต่อกับระบบภายในและเครื่องมือด้านความมั่นคงปลอดภัยขององค์กร เช่น Microsoft Entra ID หรือ Active Directory สำหรับการยืนยันตัวตนและบริหารผู้ใช้งาน, Configuration Management Database (CMDB) หรือ IT Asset Management สำหรับข้อมูลทรัพย์สิน, IT Service Management (ITSM) สำหรับการสร้างและติดตาม Ticket, Security Information and Event Management (SIEM) สำหรับส่งต่อเหตุการณ์และข้อมูลที่เกี่ยวข้อง, Email และ Microsoft Teams สำหรับการแจ้งเตือน รวมถึง Business Intelligence Platform สำหรับการวิเคราะห์และรายงานข้อมูลเชิงบริหาร<br><br>ระบบต้องมี Dashboard และรายงานสำหรับผู้ใช้งานแต่ละกลุ่ม เช่น Cybersecurity Team, System Owner, Application Owner, Remediation Team, Risk Owner, Auditor และ Management โดยสามารถแสดงจำนวนช่องโหว่ตามระดับความรุนแรง สถานะ หน่วยงาน ประเภททรัพย์สิน แหล่งที่มา อายุช่องโหว่ สถานะ SLA แนวโน้มความเสี่ยง ประสิทธิภาพการแก้ไข และรายการช่องโหว่สำคัญที่ต้องเร่งดำเนินการ<br><br>ระบบต้องพัฒนาในรูปแบบ Enterprise Web-based Application รองรับการใช้งานผ่าน Desktop และ Mobile Browser โดยมี Responsive User Interface และ REST API สำหรับเชื่อมต่อกับระบบอื่น รองรับการขยายจำนวนผู้ใช้งาน ปริมาณข้อมูล และแหล่งข้อมูลช่องโหว่เพิ่มเติมในอนาคต โดยไม่กระทบต่อการทำงานหลักของระบบ<br><br>ระบบต้องรองรับ Role-based Access Control (RBAC), Least Privilege, Segregation of Duties และการจำกัดการเข้าถึงข้อมูลตามหน่วยงานหรือทรัพย์สินที่ผู้ใช้งานรับผิดชอบ รวมถึงบันทึก Audit Log สำหรับกิจกรรมสำคัญ เช่น การเข้าสู่ระบบ การดูข้อมูล การเพิ่มหรือแก้ไขข้อมูล การเปลี่ยนสถานะ การมอบหมายงาน การอนุมัติ การดาวน์โหลด การ Export และการตั้งค่าระบบ<br><br>ข้อมูลสำคัญต้องได้รับการปกป้องทั้งขณะรับส่งและขณะจัดเก็บ ระบบต้องรองรับนโยบายด้าน Cybersecurity, Data Classification, Personal Data Protection และระยะเวลาการเก็บรักษาข้อมูลขององค์กร พร้อมมีความสามารถด้าน Monitoring, Logging, Backup, Recovery และ Integration Monitoring เพื่อสนับสนุนการให้บริการอย่างต่อเนื่องและสามารถตรวจสอบย้อนหลังได้ |  |  |  |
| Definitions: | Vulnerability - จุดอ่อนหรือข้อบกพร่องของระบบ แอปพลิเคชัน ซอฟต์แวร์ อุปกรณ์ หรือการตั้งค่าที่อาจถูกใช้เพื่อสร้างผลกระทบต่อความลับ ความถูกต้องครบถ้วน หรือความพร้อมใช้งานของข้อมูลและระบบ<br>Vulnerability Management - กระบวนการบริหารจัดการช่องโหว่ตั้งแต่การค้นพบ การตรวจสอบ การประเมินความเสี่ยง การมอบหมาย การแก้ไข การตรวจสอบซ้ำ จนถึงการปิดหรือยอมรับความเสี่ยง<br>Vulnerability Finding - ผลการตรวจพบจุดอ่อนจาก Scanner, Security Assessment, Penetration Testing หรือการบันทึกโดยผู้ใช้งาน<br>Vulnerability Instance - รายการช่องโหว่ที่เกิดขึ้นกับทรัพย์สิน เป้าหมาย URL, IP Address, Port, Component หรือทรัพยากรเฉพาะรายการ<br>Asset - ทรัพย์สินสารสนเทศที่อยู่ภายใต้การบริหารช่องโหว่ เช่น Application, Server, Database, Network Device, Cloud Resource, Container และอุปกรณ์ปลายทาง<br>Asset Owner - บุคคลหรือหน่วยงานที่รับผิดชอบด้านธุรกิจและการกำกับดูแลทรัพย์สิน<br>System Owner - ผู้รับผิดชอบการให้บริการ การบริหาร และการตัดสินใจเกี่ยวกับระบบสารสนเทศ<br>Application Owner - ผู้รับผิดชอบด้านการบริหารและดำเนินงานของ Application ที่เกี่ยวข้องกับช่องโหว่<br>Remediation Owner - บุคคลหรือทีมที่ได้รับมอบหมายให้ดำเนินการแก้ไขหรือลดความเสี่ยงของช่องโหว่<br>Asset Criticality - ระดับความสำคัญของทรัพย์สินต่อธุรกิจ เช่น Critical, High, Medium หรือ Low<br>Business Impact - ระดับผลกระทบต่อการดำเนินงาน รายได้ ชื่อเสียง ลูกค้า กฎหมาย หรือข้อกำหนด หากช่องโหว่ถูกใช้โจมตี<br>CVE - Common Vulnerabilities and Exposures รหัสมาตรฐานที่ใช้ระบุช่องโหว่ด้านความมั่นคงปลอดภัยที่มีการเผยแพร่<br>CWE - Common Weakness Enumeration รายการมาตรฐานสำหรับจำแนกประเภทของจุดอ่อนใน Software และ Hardware<br>CVSS - Common Vulnerability Scoring System มาตรฐานสำหรับประเมินความรุนแรงเชิงเทคนิคของช่องโหว่<br>KEV - Known Exploited Vulnerabilities รายการช่องโหว่ที่มีหลักฐานว่าถูกนำไปใช้โจมตีจริง<br>Risk Score - คะแนนความเสี่ยงที่ระบบคำนวณจากปัจจัยด้านเทคนิคและธุรกิจ เช่น CVSS, Asset Criticality, Business Impact, Exposure, Exploit Availability และ Threat Intelligence<br>Risk Level - ระดับความเสี่ยงของช่องโหว่ ได้แก่ Critical, High, Medium, Low และ Informational<br>Vulnerability Scanner - เครื่องมือสำหรับตรวจหาช่องโหว่ใน Infrastructure, Network, Operating System, Database, Application หรือ Cloud Resource<br>SAST - Static Application Security Testing การวิเคราะห์ Source Code หรือ Binary โดยไม่ต้องเรียกใช้งาน Application<br>DAST - Dynamic Application Security Testing การตรวจสอบช่องโหว่ของ Application ขณะทำงาน<br>SCA - Software Composition Analysis การตรวจสอบ Open-source Component, Library, Package, License และช่องโหว่ขององค์ประกอบซอฟต์แวร์<br>Penetration Testing - การทดสอบเจาะระบบเพื่อค้นหาและยืนยันช่องโหว่โดยจำลองเทคนิคการโจมตีภายใต้ขอบเขตที่ได้รับอนุญาต<br>Threat Intelligence - ข้อมูลเกี่ยวกับภัยคุกคาม ผู้โจมตี เทคนิคการโจมตี Exploit และแนวโน้มความเสี่ยงที่ใช้ประกอบการจัดลำดับความสำคัญ<br>Internet-facing Asset - ทรัพย์สินที่สามารถเข้าถึงได้จาก Internet โดยตรงหรือผ่านบริการที่เปิดเผยต่อภายนอก<br>Exposure - ระดับการเปิดเผยของทรัพย์สินหรือบริการต่อผู้ใช้งาน เครือข่ายภายนอก หรือ Internet<br>SLA - Service Level Agreement กรอบระยะเวลาที่กำหนดสำหรับการรับทราบ เริ่มดำเนินการ และแก้ไขช่องโหว่<br>SLA Breach - เหตุการณ์ที่การรับทราบ การเริ่มดำเนินการ หรือการแก้ไขช่องโหว่เกินระยะเวลาที่กำหนด<br>Remediation - การแก้ไขต้นเหตุของช่องโหว่ เช่น Patch, Upgrade, Configuration Change หรือ Source Code Modification<br>Mitigation - การลดโอกาสหรือผลกระทบของช่องโหว่ โดยอาจยังไม่ได้แก้ไขต้นเหตุอย่างสมบูรณ์<br>Compensating Control - มาตรการควบคุมทดแทนที่ใช้ลดความเสี่ยงเมื่อยังไม่สามารถแก้ไขช่องโหว่ได้โดยตรง<br>Risk Acceptance - การยอมรับความเสี่ยงอย่างเป็นทางการภายในระยะเวลาที่กำหนด โดยผ่านการพิจารณาและอนุมัติจากผู้มีอำนาจ<br>Security Exception - การขอยกเว้นจากนโยบาย มาตรฐาน หรือกรอบระยะเวลาที่กำหนด โดยต้องมีเหตุผล มาตรการควบคุม และการอนุมัติ<br>SLA Extension - การขยายระยะเวลาการแก้ไขช่องโหว่จากกำหนดเดิมตามเงื่อนไขและ Approval Matrix<br>False Positive - รายการที่เครื่องมือหรือผู้ตรวจสอบรายงานว่าเป็นช่องโหว่ แต่ภายหลังได้รับการยืนยันว่าไม่ใช่ช่องโหว่จริงในบริบทของทรัพย์สินนั้น<br>Duplicate Finding - รายการช่องโหว่ที่ซ้ำกับรายการเดิม โดยอาจมาจาก Scanner เดียวกันหรือหลายแหล่งข้อมูล<br>Recurring Vulnerability - ช่องโหว่ที่เคยได้รับการแก้ไขหรือปิดแล้ว แต่ถูกตรวจพบอีกครั้งในทรัพย์สินหรือองค์ประกอบเดิม<br>Rescan - การตรวจสอบซ้ำด้วย Scanner หลังจากมีการแก้ไขหรือดำเนินมาตรการลดความเสี่ยง<br>Verification - กระบวนการตรวจสอบหลักฐานหรือผลการ Rescan เพื่อยืนยันว่าช่องโหว่ได้รับการแก้ไขแล้ว<br>Evidence - หลักฐานสนับสนุนการแก้ไข การตรวจสอบ การยอมรับความเสี่ยง หรือการอนุมัติ เช่น Screenshot, Scan Result, Configuration, Log หรือเอกสาร<br>Workflow - ลำดับสถานะ กิจกรรม เงื่อนไข และการอนุมัติที่ใช้ควบคุมกระบวนการบริหารช่องโหว่<br>Approval Matrix - กฎที่กำหนดผู้อนุมัติตามระดับความเสี่ยง ประเภทคำขอ ระดับทรัพย์สิน และหน่วยงาน<br>Escalation - การแจ้งเตือนหรือยกระดับประเด็นไปยังผู้มีอำนาจสูงขึ้น เมื่อรายการใกล้ครบกำหนด เกิน SLA หรือมีความเสี่ยงสูง<br>RBAC Role-based Access Control - การควบคุมสิทธิ์ตามบทบาทของผู้ใช้งาน<br>Least Privilege - หลักการให้สิทธิ์แก่ผู้ใช้งานเท่าที่จำเป็นต่อการปฏิบัติหน้าที่<br>Segregation of Duties - การแบ่งแยกหน้าที่เพื่อป้องกันบุคคลเดียวเป็นผู้ร้องขอ ตรวจสอบ และอนุมัติรายการเดียวกันโดยไม่มีการควบคุม<br>Data Segregation - การจำกัดการเข้าถึงข้อมูลตามหน่วยงาน ทรัพย์สิน บทบาท หรือขอบเขตความรับผิดชอบ<br>Audit Log - บันทึกกิจกรรมของผู้ใช้งาน ระบบ และการเชื่อมต่อ เช่น การดู แก้ไข เปลี่ยนสถานะ อนุมัติ และ Export<br>CMDB Configuration Management Database - ฐานข้อมูลสำหรับจัดเก็บข้อมูล Configuration Item, Asset, Service และความสัมพันธ์<br>ITSM IT Service Management - ระบบสำหรับบริหาร Incident, Service Request, Problem, Change และงานบริการด้าน IT<br>SIEM Security Information and Event Management - ระบบจัดเก็บ วิเคราะห์ และเชื่อมโยง Security Event และ Log<br>MTTA Mean Time to Acknowledge - ระยะเวลาเฉลี่ยตั้งแต่ตรวจพบช่องโหว่จนถึงการรับทราบรายการ<br>MTTR Mean Time to Remediate - ระยะเวลาเฉลี่ยตั้งแต่ตรวจพบช่องโหว่จนถึงการแก้ไขหรือปิดรายการ<br>API Application Programming Interface - ช่องทางมาตรฐานสำหรับแลกเปลี่ยนข้อมูลระหว่างระบบ<br>Data Retention - ระยะเวลาและหลักเกณฑ์ในการเก็บรักษาข้อมูลก่อนจัดเก็บถาวรหรือทำลาย<br>Archive - การย้ายข้อมูลที่ไม่อยู่ระหว่างดำเนินการออกจากชุดข้อมูลใช้งานหลัก โดยยังสามารถเรียกดูตามสิทธิ์ได้ |  |  |  |
| OVERVIEW DESCRIPTION |  |  |  |  |
| Product Perspective: | ระบบ Security Vulnerability Management System เป็น Enterprise Web-based Application ที่ทำหน้าที่เป็นแพลตฟอร์มกลางขององค์กรสำหรับรวบรวม ประมวลผล บริหาร และติดตามข้อมูลช่องโหว่จากหลายแหล่ง โดยระบบไม่ได้ทำหน้าที่แทน Vulnerability Scanner หรือเครื่องมือ Security Testing แต่ทำหน้าที่รวมผลการตรวจสอบเข้ากับข้อมูลทรัพย์สิน เจ้าของระบบ บริบททางธุรกิจ และกระบวนการกำกับดูแลขององค์กร |  |  |  |
| User Classes: | 1. System Administrator<br><br>หน้าที่หลัก<br><br>จัดการ Configuration ของระบบ<br>จัดการ User, Role และ Permission<br>จัดการ Master Data<br>กำหนด Workflow, SLA และ Notification Template<br>ตรวจสอบ System Health และ Integration Status<br><br>สิทธิ์สำคัญ<br><br>เข้าถึงเมนู Administration<br>เพิ่ม แก้ไข และปิดการใช้งาน Configuration<br>กำหนดสิทธิ์และขอบเขตข้อมูล<br>ตรวจสอบ System Log ตามสิทธิ์<br><br>ข้อจำกัด<br><br>ไม่ควรอนุมัติ Risk Acceptance หรือ False Positive เว้นแต่ได้รับ Business Role เพิ่มเติม<br>ไม่สามารถแก้ไขหรือลบ Audit Log<br>ไม่ควรเข้าถึงข้อมูลเชิงธุรกิจที่อยู่นอกความจำเป็นในการดูแลระบบ<br>2. Cybersecurity Administrator<br><br>หน้าที่หลัก<br><br>กำหนดมาตรฐาน Vulnerability Management<br>ดูแล Risk Model, Severity Mapping และ SLA Policy<br>กำหนด Scanner Source และ Integration Rule<br>ดูแล Workflow และ Escalation Matrix<br><br>สิทธิ์สำคัญ<br><br>จัดการ Security Master Data<br>ปรับแต่ง Risk Scoring Configuration<br>ดูข้อมูลช่องโหว่ในภาพรวมขององค์กร<br>ตรวจสอบรายการ Critical และ KEV<br><br>ข้อจำกัด<br><br>การปรับ Risk Score รายกรณีต้องระบุเหตุผล<br>การเปลี่ยนนโยบายสำคัญต้องผ่าน Change Control<br>ไม่สามารถแก้ไข Audit Log<br>3. Vulnerability Management Officer<br><br>หน้าที่หลัก<br><br>ตรวจสอบและคัดกรอง Vulnerability Finding<br>ยืนยันหรือปฏิเสธรายการ<br>ตรวจสอบ Duplicate Finding<br>มอบหมาย Remediation Owner<br>ติดตาม SLA และดำเนินการ Escalation<br>ประสานงานกับ Asset Owner และทีมแก้ไข<br><br>สิทธิ์สำคัญ<br><br>ดูและแก้ไขข้อมูลช่องโหว่<br>เปลี่ยนสถานะตาม Workflow<br>มอบหมายและเปลี่ยนผู้รับผิดชอบ<br>สร้าง Remediation Task<br>ขอ Rescan และดำเนินการ Verification<br><br>ข้อจำกัด<br><br>ไม่สามารถอนุมัติ Risk Acceptance ที่ตนเองเป็นผู้ยื่น<br>ไม่สามารถปิด Critical Vulnerability โดยไม่มี Verification<br>การเปลี่ยน Severity หรือ Risk Score ต้องมีเหตุผลและ Audit Log<br>4. Security Analyst<br><br>หน้าที่หลัก<br><br>วิเคราะห์รายละเอียดเชิงเทคนิคของช่องโหว่<br>ตรวจสอบ CVE, CWE, CVSS และ Exploit Information<br>ยืนยันผลการตรวจพบ<br>ตรวจสอบหลักฐานและผลการแก้ไข<br>ดำเนินการ Verification หรือเสนอให้ Reopen<br><br>สิทธิ์สำคัญ<br><br>เข้าถึง Technical Finding และ Evidence<br>เพิ่ม Technical Comment และ Recommendation<br>ปรับสถานะ Under Review, Confirmed หรือ Pending Verification<br>ยืนยัน Resolved หรือเสนอ Reopened<br><br>ข้อจำกัด<br><br>ไม่สามารถอนุมัติ Risk Acceptance แทน Risk Owner<br>ไม่สามารถแก้ไขข้อมูล Asset Master โดยไม่มีสิทธิ์เพิ่มเติม<br>ต้องไม่เป็นผู้อนุมัติผลที่ตนเองดำเนินการในกรณีที่กำหนด Independent Verification<br>5. SOC Analyst<br><br>หน้าที่หลัก<br><br>ตรวจสอบช่องโหว่ที่สัมพันธ์กับ Threat Intelligence หรือ Security Incident<br>แจ้งข้อมูล Active Exploitation และ Indicator ที่เกี่ยวข้อง<br>ติดตาม Critical Vulnerability และ KEV<br>ประสานงานกรณีช่องโหว่มีหลักฐานการโจมตีจริง<br><br>สิทธิ์สำคัญ<br><br>ดูข้อมูล Critical, High และ KEV<br>เพิ่ม Threat Context และ Operational Comment<br>สร้าง Escalation หรือเชื่อมโยง Security Incident<br>เข้าถึง Dashboard ที่เกี่ยวข้องกับ Threat Exposure<br><br>ข้อจำกัด<br><br>ไม่สามารถเปลี่ยน Ownership ของ Asset<br>ไม่สามารถอนุมัติ Closure หรือ Risk Acceptance หากไม่ได้รับ Role เพิ่มเติม<br>6. Asset Owner<br><br>หน้าที่หลัก<br><br>รับผิดชอบความเสี่ยงที่เกี่ยวข้องกับ Asset<br>ยืนยันข้อมูล Asset, Ownership และ Criticality<br>กำกับดูแลการแก้ไขช่องโหว่<br>พิจารณาหรือสนับสนุนคำขอ Risk Acceptance<br><br>สิทธิ์สำคัญ<br><br>ดูช่องโหว่ของ Asset ที่รับผิดชอบ<br>มอบหมายทีมดำเนินการภายในขอบเขต<br>ติดตาม SLA และ Remediation Progress<br>ยื่นหรือรับรอง Business Justification<br><br>ข้อจำกัด<br><br>ไม่สามารถดูข้อมูล Asset ของหน่วยงานอื่นหากไม่ได้รับสิทธิ์<br>ไม่สามารถแก้ไข Technical Finding ต้นทาง<br>ไม่สามารถอนุมัติคำขอของตนเองเมื่อขัดกับ Segregation of Duties<br>7. System Owner / Application Owner<br><br>หน้าที่หลัก<br><br>รับผิดชอบระบบหรือ Application ที่มีช่องโหว่<br>ตรวจสอบผลกระทบและวางแผนแก้ไข<br>ประสานงานกับ Infrastructure หรือ Development Team<br>ยืนยัน Downtime, Dependency และ Change Window<br><br>สิทธิ์สำคัญ<br><br>ดูรายการช่องโหว่ของระบบที่รับผิดชอบ<br>สร้างและปรับปรุง Remediation Plan<br>มอบหมายงานให้ Technical Team<br>แนบหลักฐานและส่งรายการเพื่อ Verification<br>ยื่นคำขอ SLA Extension หรือ Risk Acceptance<br><br>ข้อจำกัด<br><br>ไม่สามารถปิดช่องโหว่โดยตรงโดยไม่มี Verification<br>ไม่สามารถอนุมัติ Risk Acceptance ของระบบตนเองหาก Approval Matrix ไม่อนุญาต<br>ไม่สามารถปรับ Risk Score โดยไม่มีสิทธิ์และเหตุผล<br>8. Remediation Owner<br><br>หน้าที่หลัก<br><br>ดำเนินการแก้ไขหรือลดความเสี่ยง<br>บันทึกแผนและความคืบหน้า<br>แนบหลักฐานการดำเนินงาน<br>แจ้งพร้อมตรวจสอบซ้ำ<br><br>สิทธิ์สำคัญ<br><br>ดู Technical Detail ที่จำเป็นต่อการแก้ไข<br>ปรับสถานะ Assigned เป็น In Progress<br>เพิ่ม Comment และ Evidence<br>ส่งรายการเข้าสู่ Pending Verification<br>เชื่อมโยง Change Request หรือ ITSM Ticket<br><br>ข้อจำกัด<br><br>ไม่สามารถอนุมัติหรือปิดรายการที่ตนเองแก้ไข<br>ไม่สามารถเข้าถึงช่องโหว่นอกขอบเขตที่ได้รับมอบหมาย<br>ไม่สามารถแก้ไขข้อมูล Scanner ต้นทาง<br>9. Infrastructure Team<br><br>หน้าที่หลัก<br><br>แก้ไขช่องโหว่ของ Server, Operating System, Database, Network และ Cloud Infrastructure<br>ดำเนินการ Patch, Upgrade หรือ Configuration Change<br>ประสานงาน Downtime และ Change Management<br>ส่งหลักฐานและผลการดำเนินงาน<br><br>ขอบเขตข้อมูล<br><br>ช่องโหว่ของ Infrastructure Asset ที่ทีมรับผิดชอบ<br>Technical Recommendation, Port, Service, Package และ Configuration<br>SLA และ Remediation Task ที่เกี่ยวข้อง<br>10. Application Development Team<br><br>หน้าที่หลัก<br><br>แก้ไขช่องโหว่จาก SAST, DAST และ SCA<br>ปรับปรุง Source Code, Library, Dependency และ Application Configuration<br>เชื่อมโยงงานกับ Development Backlog หรือ CI/CD Pipeline<br>ส่ง Build หรือ Release เพื่อ Verification<br><br>ขอบเขตข้อมูล<br><br>Source Code Finding และ Application Finding ที่ได้รับมอบหมาย<br>CWE, Code Location, Component และ Dependency<br>Evidence และผลจาก Security Testing รอบใหม่<br><br>ข้อจำกัด<br><br>การเข้าถึง Source Code Detail ต้องจำกัดตาม Project<br>ไม่สามารถดู Finding ของ Application อื่นโดยไม่มีสิทธิ์<br>11. Risk Owner<br><br>หน้าที่หลัก<br><br>พิจารณาความเสี่ยงและผลกระทบต่อธุรกิจ<br>อนุมัติหรือปฏิเสธ Risk Acceptance ตาม Approval Matrix<br>กำหนดเงื่อนไขและระยะเวลาการยอมรับความเสี่ยง<br>ติดตาม Compensating Control<br><br>สิทธิ์สำคัญ<br><br>เข้าถึง Risk Assessment และ Business Justification<br>อนุมัติ ปฏิเสธ หรือส่งกลับให้แก้ไข<br>กำหนดวันหมดอายุและเงื่อนไขการทบทวน<br>ดูรายการ Risk Acceptance ที่ใกล้หมดอายุ<br><br>ข้อจำกัด<br><br>ไม่สามารถอนุมัติคำขอที่มี Conflict of Interest<br>การอนุมัติ Critical Risk อาจต้องมีผู้อนุมัติหลายระดับ<br>ไม่สามารถลบประวัติการอนุมัติ<br>12. Approver<br><br>หน้าที่หลัก<br><br>พิจารณา False Positive, SLA Extension, Exception, Closure หรือ Risk Acceptance ตามประเภทคำขอ<br>ตรวจสอบข้อมูล เหตุผล หลักฐาน และผลกระทบ<br>บันทึกความเห็นประกอบการตัดสินใจ<br><br>สิทธิ์สำคัญ<br><br>อนุมัติ ปฏิเสธ หรือส่งกลับคำขอ<br>เข้าถึงข้อมูลที่จำเป็นต่อการพิจารณา<br>ดู Approval Worklist และประวัติการอนุมัติ<br><br>ข้อจำกัด<br><br>ไม่สามารถอนุมัติคำขอที่ตนเป็นผู้สร้าง<br>ต้องดำเนินการภายใน Approval SLA<br>การตัดสินใจทุกครั้งต้องถูกบันทึกใน Audit Log<br>13. Auditor / Compliance Officer<br><br>หน้าที่หลัก<br><br>ตรวจสอบการปฏิบัติตามนโยบายและข้อกำหนด<br>ตรวจสอบ Audit Trail, SLA, Approval และ Evidence<br>ตรวจสอบสิทธิ์และ Segregation of Duties<br>จัดทำ Compliance Report<br><br>สิทธิ์สำคัญ<br><br>อ่านข้อมูลตามขอบเขตที่ได้รับอนุญาต<br>ค้นหาและ Export Audit Record<br>ดูประวัติการเปลี่ยนแปลงและการอนุมัติ<br><br>ข้อจำกัด<br><br>เป็นสิทธิ์แบบ Read-only โดยหลัก<br>ไม่สามารถเปลี่ยนสถานะหรือแก้ไขข้อมูลช่องโหว่<br>การ Export ต้องได้รับอนุญาตและถูกบันทึก<br>14. Management / Executive<br><br>หน้าที่หลัก<br><br>ติดตามภาพรวมความเสี่ยงและประสิทธิภาพการแก้ไข<br>ติดตาม Critical Vulnerability, KEV และ SLA Breach<br>กำกับหน่วยงานที่มีรายการค้างหรือความเสี่ยงสูง<br>ใช้ข้อมูลประกอบการตัดสินใจและจัดสรรทรัพยากร<br><br>สิทธิ์สำคัญ<br><br>เข้าถึง Executive Dashboard<br>ดูข้อมูลสรุปตาม Business Unit, Risk Level และ Trend<br>Drill-down ตามสิทธิ์และความจำเป็น<br>รับ Executive Notification และ Escalation<br><br>ข้อจำกัด<br><br>ไม่สามารถแก้ไข Technical Finding<br>ข้อมูลรายละเอียดอาจถูก Mask ตาม Data Classification<br>การ Export รายละเอียดต้องเป็นไปตามสิทธิ์<br>15. External Security Assessor<br><br>หน้าที่หลัก<br><br>บันทึกหรือส่งผลจาก Penetration Testing และ Security Assessment<br>ให้ Recommendation และ Evidence<br>ตอบข้อซักถามเกี่ยวกับ Finding<br><br>ข้อจำกัด<br><br>เข้าถึงเฉพาะ Project และข้อมูลที่ได้รับมอบหมาย<br>ไม่สามารถดูข้อมูลช่องโหว่อื่นขององค์กร<br>ไม่สามารถปิดรายการหรืออนุมัติ Risk Acceptance<br>บัญชีต้องมีวันหมดอายุและผ่านการอนุมัติ<br>16. External System / API Client<br><br>หน้าที่หลัก<br><br>ส่งหรือรับข้อมูลผ่าน API<br>ส่ง Scanner Result, Asset Data หรือ Ticket Status<br>รับ Vulnerability Status หรือข้อมูลที่ได้รับอนุญาต<br><br>ข้อควบคุม<br><br>ใช้ Service Account หรือ Machine Identity<br>จำกัด Scope ของ API<br>ใช้ Authentication และ Encryption ที่องค์กรอนุมัติ<br>กำหนด Rate Limit, Timeout และ Retry Policy<br>บันทึก Integration Log และ Audit Log |  |  |  |
| Assumptions & Constraints: | 1.องค์กรมีนโยบายและกระบวนการ Vulnerability Management ที่ได้รับการอนุมัติ หรือจะจัดทำให้แล้วเสร็จก่อน Production Go-live<br>2.องค์กรมีแหล่งข้อมูล Asset ที่เชื่อถือได้ เช่น CMDB หรือ IT Asset Management และมี Unique Identifier สำหรับเชื่อมโยงข้อมูล<br>3.Asset ที่อยู่ในขอบเขตต้องมี Asset Owner, System Owner หรือหน่วยงานรับผิดชอบที่สามารถระบุได้<br>4.องค์กรจะกำหนด Asset Criticality, Business Impact และ Data Classification สำหรับทรัพย์สินที่สำคัญ<br>5.Scanner และ Security Tool ที่เกี่ยวข้องสามารถส่งข้อมูลผ่าน REST API, Webhook หรือไฟล์มาตรฐาน เช่น CSV, XLSX หรือ JSON<br>6.Source System มีข้อมูลเพียงพอสำหรับการทำ Asset Mapping และ Deduplication เช่น IP Address, Hostname, URL, Application ID, Cloud Resource ID หรือ Repository ID<br>7.ผู้ใช้งานภายในองค์กรมีบัญชีใน Microsoft Entra ID หรือ Active Directory และสามารถใช้ Single Sign-on ได้<br>8.Role, Permission, Organizational Structure และผู้อนุมัติสามารถนำเข้าหรือกำหนดในระบบได้ก่อนเริ่มใช้งาน<br>9.องค์กรจะกำหนด Risk Scoring Model, Risk Threshold, SLA และ Approval Matrix ที่เหมาะสมกับบริบทธุรกิจ<br>10.ทีม Cybersecurity มีหน้าที่ตรวจสอบคุณภาพข้อมูลและกำกับกระบวนการช่องโหว่<br>11.Asset Owner และ Remediation Owner มีหน้าที่ปรับปรุงสถานะ แผนการแก้ไข และหลักฐานภายในระยะเวลาที่กำหนด<br>12.ITSM สามารถรับคำขอสร้าง Ticket และส่งสถานะกลับผ่าน API หรือช่องทาง Integration ที่ตกลงร่วมกัน<br>13.Email และ Microsoft Teams พร้อมใช้งานสำหรับการแจ้งเตือนและ Escalation<br>14.เส้นทาง Network, Firewall Rule, DNS, Certificate และ Credential ที่จำเป็นต่อ Integration จะได้รับการจัดเตรียมโดยหน่วยงานที่เกี่ยวข้อง<br>15.ข้อมูลย้อนหลังที่จะย้ายเข้าสู่ระบบมีรูปแบบที่สามารถตรวจสอบและแปลงได้<br>16.ข้อมูลที่ไม่มี Identifier หรือ Ownership ชัดเจนจะถูกจัดเข้าสู่คิวสำหรับ Data Reconciliation<br>17.การปิดช่องโหว่ต้องมีหลักฐานหรือผล Verification ตามระดับความเสี่ยง<br>18.ช่องโหว่ระดับ Critical ต้องผ่าน Independent Verification ก่อนปิดรายการ<br>19.Risk Acceptance, False Positive และ Security Exception ต้องมีวันหมดอายุหรือวันทบทวน<br>20.Requirement ที่ยังไม่ได้รับการยืนยันจะถูกระบุเป็น Assumption หรือ TBD และต้องผ่านการอนุมัติก่อนนำไปพัฒนา |  |  |  |
| TECH STACK |  |  |  |  |
| Frontend | React + TypeScript, Plotly/ECharts |  |  |  |
| API | FastAPI, WebSocket/SSE |  |  |  |
| Queue/Worker | Celery + Redis or RabbitMQ |  |  |  |
| ML | scikit-learn, XGBoost, LightGBM, CatBoost, Optuna |  |  |  |
| Tracking | MLflow |  |  |  |
| Storage | SQLite, File system/MinIO |  |  |  |
| Deploy | Dockerfile |  |  |  |
| FUNCTIONAL REQUIREMENTS (FR) |  |  |  |  |
| FR# | Topic | Function Name | Function Detail | Note |
| FR-01 | Asset Management | Asset Inventory | ระบบต้องมีหน้าจอแสดงรายการทรัพย์สินที่อยู่ภายใต้กระบวนการ Vulnerability Management โดยแสดงอย่างน้อย Asset ID, Asset Name, Asset Type, IP Address, Hostname, Environment, Business Unit, Asset Owner, System Owner, Criticality, Internet Exposure และสถานะของ Asset |  |
| FR-02 | Asset Management | Asset Synchronization | เมื่อถึงรอบเวลาที่กำหนด ระบบต้องรับและปรับปรุงข้อมูล Asset จาก CMDB หรือ IT Asset Management โดยต้องบันทึกผลการ Synchronize จำนวนรายการสำเร็จ ล้มเหลว และรายการที่ไม่สามารถ Mapping ได้ |  |
| FR-03 | Asset Management | Asset Ownership Mapping | เมื่อมี Asset ใหม่เข้าสู่ระบบ ระบบต้องเชื่อมโยง Asset Owner, System Owner, Application Owner และ Remediation Team จากข้อมูลต้นทางหรือ Assignment Rule |  |
| FR-04 | Asset Management | Asset Criticality | ระบบต้องรองรับการกำหนดระดับความสำคัญของ Asset อย่างน้อย Critical, High, Medium และ Low พร้อมบันทึกผู้แก้ไข วันที่ และเหตุผล |  |
| FR-05 | Data Collection | Vulnerability Data Import | เมื่อได้รับไฟล์ที่อยู่ในรูปแบบที่รองรับ ระบบต้องตรวจสอบโครงสร้างและนำเข้าข้อมูล Vulnerability Finding พร้อมแสดงผลการนำเข้าเป็น Success, Partial Success หรือ Failed |  |
| FR-06 | Data Collection | Scanner API Integration | เมื่อถึงรอบเวลาหรือได้รับ Trigger จาก Scanner ระบบต้องเรียก API เพื่อรับ Scan Result และบันทึก Integration Log |  |
| FR-07 | Data Collection | Manual Finding Registration | ผู้มีสิทธิ์ต้องสามารถบันทึก Finding จาก Penetration Test, Manual Assessment หรือแหล่งข้อมูลที่ไม่มี Integration ได้ |  |
| FR-08 | Data Collection | Import Template Management | ระบบต้องมี Template กลางสำหรับนำเข้าข้อมูล พร้อมระบุ Mandatory Field, Data Type และรูปแบบข้อมูลที่รองรับ |  |
| FR-09 | Data Quality | Data Validation | เมื่อระบบได้รับ Finding ระบบต้องตรวจสอบ Mandatory Field, Data Type, Asset Reference, Severity, CVSS และ Source Identifier ก่อนจัดเก็บ |  |
| FR-10 | Data Quality | Data Normalization | ระบบต้องแปลงข้อมูลจาก Scanner แต่ละแหล่งให้อยู่ใน Common Data Model เดียวกัน |  |
| FR-11 | Data Quality | Duplicate Detection | เมื่อระบบได้รับ Finding ใหม่ ระบบต้องตรวจสอบรายการซ้ำโดยใช้ Asset, Vulnerability Identifier, Location, Port, URL, Parameter, Component และ Source ตาม Rule ที่กำหนด |  |
| FR-12 | Data Quality | Duplicate Consolidation | เมื่อตรวจพบ Finding ซ้ำ ระบบต้องเชื่อมโยง Finding ใหม่กับ Vulnerability Instance เดิม และปรับ Last Detected Date โดยไม่สร้าง Remediation Task ซ้ำ |  |
| FR-13 | Reference Data | CVE and CWE Mapping | เมื่อ Finding มี CVE หรือ CWE ระบบต้องเชื่อมโยงกับข้อมูลอ้างอิงและแสดงรายละเอียดที่เกี่ยวข้อง |  |
| FR-14 | Reference Data | CVSS Mapping | ระบบต้องรองรับ CVSS Version ที่องค์กรอนุมัติ และจัดเก็บ Base Score, Vector String และ Severity |  |
| FR-15 | Threat Enrichment | Threat Intelligence Enrichment | เมื่อ Finding มี CVE ระบบต้องสามารถเพิ่มข้อมูล Exploit Availability, KEV Status, Threat Activity และข้อมูล Threat Intelligence ที่เกี่ยวข้อง |  |
| FR-16 | Risk Assessment | Risk Score Calculation | เมื่อ Finding ผ่าน Validation ระบบต้องคำนวณ Risk Score ช่วง 0 ถึง 100 จาก CVSS, Asset Criticality, Business Impact, Internet Exposure, Exploit Availability, KEV, Threat Intelligence, Vulnerability Age และ Existing Control |  |
| FR-17 | Risk Assessment | Risk Level Classification | ระบบต้องจำแนก Risk Level เป็น Critical, High, Medium, Low หรือ Informational ตามคะแนนและ Rule ที่กำหนด |  |
| FR-18 | Risk Assessment | KEV Prioritization | หาก CVE อยู่ในรายการ Known Exploited Vulnerabilities ระบบต้องเพิ่ม Priority และแสดง KEV Indicator อย่างชัดเจน |  |
| FR-19 | Risk Assessment | Internet-facing Prioritization | หาก Asset เป็น Internet-facing และ Finding มีระดับ High หรือ Critical ระบบต้องยกระดับการแจ้งเตือนตาม Escalation Matrix |  |
| FR-20 | Risk Assessment | Manual Risk Adjustment | ผู้มีสิทธิ์ต้องสามารถขอปรับ Risk Score หรือ Risk Level พร้อมระบุเหตุผล หลักฐาน ค่าเดิม และค่าใหม่ |  |
| FR-21 | Risk Assessment | Risk Recalculation | เมื่อข้อมูล Asset Criticality, Threat Intelligence, KEV หรือ Compensating Control เปลี่ยน ระบบต้องคำนวณ Risk Score ใหม่ |  |
| FR-22 | Workflow | Vulnerability Status Management | ระบบต้องรองรับสถานะ New, Under Review, Confirmed, Assigned, In Progress, Pending Verification, Resolved, Closed, Reopened, False Positive, Risk Accepted, Exception Approved และ Archived |  |
| FR-23 | Workflow | Status Transition Control | เมื่อผู้ใช้งานขอเปลี่ยนสถานะ ระบบต้องตรวจสอบ Source Status, Target Status, Permission, Mandatory Field, Evidence และ Approval ก่อนดำเนินการ |  |
| FR-24 | Assignment | Automatic Assignment | เมื่อ Finding ได้รับการยืนยัน ระบบต้องมอบหมายให้ Owner หรือ Remediation Team ตาม Asset, Business Unit, Technology และ Assignment Rule |  |
| FR-25 | Assignment | Manual Assignment | Vulnerability Management Officer ต้องสามารถมอบหมายหรือเปลี่ยนผู้รับผิดชอบ พร้อมระบุเหตุผล |  |
| FR-26 | Assignment | Bulk Assignment | ผู้มีสิทธิ์ต้องสามารถมอบหมาย Finding หลายรายการให้บุคคลหรือทีมเดียวกัน |  |
| FR-27 | Remediation | Remediation Task | เมื่อ Finding ถูกมอบหมาย ระบบต้องสร้าง Remediation Task ที่มี Task ID, Owner, Due Date, Risk Level, Recommendation และสถานะ |  |
| FR-28 | Remediation | Remediation Plan | Remediation Owner ต้องสามารถบันทึก Root Cause, Action Plan, Target Date, Change Reference และ Dependency ได้ |  |
| FR-29 | Remediation | Remediation Progress | Remediation Owner ต้องสามารถปรับปรุงเปอร์เซ็นต์ความคืบหน้า สถานะ ความเห็น และอุปสรรค |  |
| FR-30 | Collaboration | Comment and Mention | ผู้ใช้งานที่มีสิทธิ์ต้องสามารถเพิ่ม Comment และ Mention ผู้เกี่ยวข้องในรายการช่องโหว่ได้ |  |
| FR-31 | Evidence | Evidence Management | ระบบต้องรองรับการแนบหลักฐาน เช่น Scan Result, Screenshot, Configuration, Change Record และเอกสารรับรอง |  |
| FR-32 | ITSM Integration | Ticket Creation | เมื่อเข้าเงื่อนไขที่กำหนด ระบบต้องสามารถสร้าง ITSM Ticket พร้อมส่ง Asset, Finding, Risk Level, Recommendation, SLA และ Owner |  |
| FR-33 | ITSM Integration | Ticket Status Synchronization | ระบบต้องรับสถานะและข้อมูลความคืบหน้าจาก ITSM และเชื่อมโยงกับ Remediation Task |  |
| FR-34 | Workflow | Bulk Status Update | ผู้มีสิทธิ์ต้องสามารถเปลี่ยนสถานะหลายรายการได้ หากทุกรายการผ่าน Validation และ Transition Rule |  |
| FR-35 | SLA Management | SLA Calculation | เมื่อ Finding ได้รับ Risk Level ระบบต้องคำนวณ Acknowledgement Due Date, Remediation Start Date และ Resolution Due Date ตาม SLA Policy |  |
| FR-36 | SLA Management | SLA Timer | ระบบต้องแสดงเวลาคงเหลือ สถานะใกล้ครบกำหนด และสถานะ SLA Breach |  |
| FR-37 | SLA Management | SLA Recalculation | เมื่อ Risk Level หรือ SLA Policy เปลี่ยน ระบบต้องคำนวณ SLA ใหม่โดยเก็บค่าเดิมและเหตุผล |  |
| FR-38 | Notification | Assignment Notification | เมื่อ Finding หรือ Task ถูกมอบหมาย ระบบต้องแจ้งผู้รับผิดชอบผ่าน In-app Notification, Email หรือ Microsoft Teams |  |
| FR-39 | Notification | SLA Reminder | ระบบต้องแจ้งเตือนก่อนครบกำหนด SLA ตามช่วงเวลาที่กำหนด |  |
| FR-40 | Escalation | Overdue Escalation | เมื่อ Finding เกิน SLA ระบบต้องแจ้ง Remediation Owner, Asset Owner และผู้บังคับบัญชาตาม Escalation Matrix |  |
| FR-41 | Notification | Critical Vulnerability Notification | เมื่อพบ Critical Finding, KEV หรือ Active Exploitation ระบบต้องแจ้งผู้เกี่ยวข้องตามช่องทางเร่งด่วน |  |
| FR-42 | Notification | Notification History | ระบบต้องจัดเก็บประวัติ Notification, ผู้รับ, ช่องทาง, เวลา และ Delivery Status |  |
| FR-43 | Verification | Submit for Verification | เมื่อ Remediation Owner ดำเนินการเสร็จ ต้องสามารถแนบหลักฐานและส่ง Finding ไปสถานะ Pending Verification |  |
| FR-44 | Verification | Rescan Request | Security Analyst ต้องสามารถสั่งหรือขอ Rescan ผ่าน Scanner Integration หรือสร้าง Manual Rescan Request |  |
| FR-45 | Verification | Verification Result | ผู้ตรวจสอบต้องสามารถบันทึกผล Verified, Not Resolved หรือ Partially Resolved พร้อมหลักฐาน |  |
| FR-46 | Closure | Vulnerability Closure | เมื่อผล Verification ยืนยันว่าไม่พบช่องโหว่ ระบบต้องอนุญาตให้ปิดรายการตาม Approval Rule |  |
| FR-47 | Closure | Vulnerability Reopen | เมื่อ Scanner ตรวจพบช่องโหว่เดิมอีกครั้ง หรือ Verification ไม่ผ่าน ระบบต้องเปิดรายการกลับมาและแจ้งผู้รับผิดชอบ |  |
| FR-48 | False Positive | False Positive Request | ผู้มีสิทธิ์ต้องสามารถยื่นคำขอ False Positive พร้อมเหตุผล หลักฐาน และวันทบทวน |  |
| FR-49 | False Positive | False Positive Approval | Approver ต้องสามารถอนุมัติ ปฏิเสธ หรือส่งกลับคำขอ False Positive |  |
| FR-50 | Risk Acceptance | Risk Acceptance Request | Asset Owner หรือ System Owner ต้องสามารถยื่นคำขอยอมรับความเสี่ยง พร้อม Business Justification, Impact, Compensating Control และ Expiry Date |  |
| FR-51 | Risk Acceptance | Risk Acceptance Approval | ระบบต้องส่งคำขอไปยัง Risk Owner และ Approver ตาม Risk Level และ Approval Matrix |  |
| FR-52 | Risk Acceptance | Expiry Monitoring | ระบบต้องแจ้งเตือนก่อนวันหมดอายุของ Risk Acceptance และเปิด Finding กลับเข้าสู่ Workflow เมื่อหมดอายุ |  |
| FR-53 | Exception | SLA Extension Request | ผู้รับผิดชอบต้องสามารถขอขยาย SLA พร้อมเหตุผล แผนแก้ไข วันที่ใหม่ และมาตรการลดความเสี่ยง |  |
| FR-54 | Exception | Security Exception Approval | ระบบต้องรองรับการอนุมัติ Security Exception ตามประเภท ความเสี่ยง หน่วยงาน และระยะเวลาที่กำหนด |  |
| FR-55 | Compensating Control | Control Management | ระบบต้องจัดเก็บ Compensating Control, Control Owner, Effectiveness, Evidence, Review Date และ Approval Status |  |
| FR-56 | Dashboard | Executive Dashboard | ระบบต้องแสดงภาพรวม Open Vulnerabilities, Risk Distribution, Critical Trend, SLA Compliance, KEV, Top Risk Assets และ Remediation Performance |  |
| FR-57 | Dashboard | Operations Dashboard | ระบบต้องแสดงงานค้าง รายการใหม่ Unassigned, Pending Verification, Overdue, Import Error และ Integration Error |  |
| FR-58 | Search | Advanced Search and Filter | ผู้ใช้ต้องสามารถค้นหาและกรองตาม Asset, Owner, Business Unit, Risk Level, CVE, CWE, Scanner, Status, SLA, Age และ Date Range |  |
| FR-59 | Reporting | Operational Report | ระบบต้องสร้างรายงาน Vulnerability Aging, SLA Breach, Remediation Status, Risk Acceptance และ Recurring Vulnerability |  |
| FR-60 | Reporting | Executive Report | ระบบต้องสร้างรายงานสรุป Trend, Risk Exposure, Business Unit Performance, MTTA, MTTR และ Top Risk |  |
| FR-61 | Reporting | Data Export | ผู้มีสิทธิ์ต้องสามารถ Export ข้อมูลตามตัวกรอง โดยระบบต้องบันทึกผู้ดำเนินการ เวลา เงื่อนไข และจำนวนรายการ |  |
| FR-62 | Access Control | Role-based Access Control | ระบบต้องควบคุมสิทธิ์การดู เพิ่ม แก้ไข มอบหมาย อนุมัติ ปิด Export และตั้งค่าตาม Role |  |
| FR-63 | Access Control | Organizational Data Segregation | ระบบต้องจำกัดการเข้าถึงข้อมูลตาม Business Unit, Asset Scope, Application Scope หรือ Responsibility Scope |  |
| FR-64 | Administration | Master Data Management | ผู้ดูแลระบบต้องจัดการ Severity, Risk Level, Asset Type, Finding Source, Status Reason และ Reference Data ได้ |  |
| FR-65 | Administration | Workflow Configuration | ผู้มีสิทธิ์ต้องกำหนด Status Transition, Mandatory Field, Required Evidence และ Approval Rule ได้ |  |
| FR-66 | Administration | SLA Policy Configuration | ผู้มีสิทธิ์ต้องกำหนด SLA ตาม Risk Level, Asset Criticality, Exposure และ KEV Status ได้ |  |
| FR-67 | Integration | Integration Monitoring | ระบบต้องแสดงสถานะ Integration, Last Successful Run, จำนวนสำเร็จ ล้มเหลว และ Error Detail |  |
| FR-68 | Audit | Audit Trail | ระบบต้องบันทึกการ Login, View, Create, Update, Assign, Status Change, Approve, Reject, Export และ Configuration Change |  |
| FR-69 | Retention | Data Archiving | เมื่อข้อมูลครบตาม Retention Policy ระบบต้อง Archive หรือทำลายข้อมูลตาม Rule ที่ได้รับอนุมัติ |  |
| FR-70 | API | External API | ระบบต้องมี REST API สำหรับส่งและรับข้อมูล Asset, Finding, Status, Task และ Integration Result ตามสิทธิ์ |  |
| NON-FUNCTIONAL REQUIREMENTS (NFR) |  |  |  |  |
| FR# | Topic | Function Name | Function Detail | Note |
| NFR-01 | Availability | Service Availability | ระบบต้องพร้อมให้บริการสำหรับผู้ใช้งานและ API ตาม Service Window ที่กำหนด |  |
| NFR-02 | Performance | Page Response Time | หน้าจอทั่วไปต้องตอบสนองภายในเวลาที่กำหนดภายใต้ภาระงานปกติ |  |
| NFR-03 | Performance | Dashboard Response Time | Dashboard มาตรฐานต้องแสดงผลภายในเวลาที่กำหนด |  |
| NFR-04 | Performance | API Response Time | Standard Transaction API ต้องตอบสนองภายในเวลาที่กำหนด โดยไม่รวมระยะเวลาของ External System |  |
| NFR-05 | Capacity | Concurrent Users | ระบบต้องรองรับผู้ใช้งานพร้อมกันโดยไม่ทำให้ Performance ต่ำกว่าเป้าหมาย |  |
| NFR-06 | Capacity | Vulnerability Data Volume | ระบบต้องจัดเก็บและค้นหา Vulnerability Record ได้ตามปริมาณที่กำหนด |  |
| NFR-07 | Capacity | Batch Import Capacity | ระบบต้องนำเข้าข้อมูลจำนวนมากแบบ Asynchronous ได้ |  |
| NFR-08 | Scalability | Horizontal Scaling | Component ที่มีภาระงานสูงต้องสามารถเพิ่ม Capacity ได้โดยไม่เปลี่ยน Business Logic |  |
| NFR-09 | Authentication | Enterprise SSO | ระบบต้องรองรับ Single Sign-on ผ่าน Identity Provider ที่องค์กรอนุมัติ |  |
| NFR-10 | Authentication | Multi-factor Authentication | ผู้ใช้งานสิทธิ์สูงและผู้ใช้งานจากภายนอกต้องใช้ MFA ตามนโยบายองค์กร |  |
| NFR-11 | Authorization | RBAC | ทุก Function และ API ต้องตรวจสอบ Role และ Data Scope ก่อนอนุญาต |  |
| NFR-12 | Authorization | Least Privilege | สิทธิ์เริ่มต้นต้องเป็นสิทธิ์ขั้นต่ำและไม่มีสิทธิ์เข้าถึงข้อมูลโดยอัตโนมัติ |  |
| NFR-13 | Authorization | Segregation of Duties | ระบบต้องแยกผู้ร้องขอ ผู้ตรวจสอบ และผู้อนุมัติในกระบวนการสำคัญ |  |
| NFR-14 | Security | Encryption in Transit | ข้อมูลที่รับส่งระหว่าง Browser, API และ External System ต้องถูกเข้ารหัส |  |
| NFR-15 | Security | Encryption at Rest | Database, Object Storage, Backup และ Log ที่มีข้อมูลสำคัญต้องถูกเข้ารหัส |  |
| NFR-16 | Security | Secrets Management | Password, API Key, Token และ Certificate Private Key ต้องไม่จัดเก็บใน Source Code หรือ Configuration แบบ Plain Text |  |
| NFR-17 | Security | Session Management | Session ต้องหมดอายุเมื่อไม่มีการใช้งาน และ Token ต้องถูกยกเลิกเมื่อออกจากระบบตามความสามารถของ Identity Provider |  |
| NFR-18 | Security | Secure File Upload | File Attachment ต้องผ่านการตรวจสอบชนิด ขนาด นามสกุล และ Malware |  |
| NFR-19 | Security | API Security | API ต้องมี Authentication, Authorization, Input Validation, Rate Limiting และ Protection Against Replay ตามความเหมาะสม |  |
| NFR-20 | Audit | Audit Logging | ระบบต้องบันทึกกิจกรรมสำคัญพร้อม User, Date-Time, Source, Action, Object, Before Value, After Value และ Result |  |
| NFR-21 | Audit | Audit Log Integrity | Audit Log ต้องไม่สามารถแก้ไขหรือลบโดยผู้ใช้งานทั่วไปและผู้ดูแล Application |  |
| NFR-22 | Privacy | Data Protection | ระบบต้องรองรับ Data Classification, Masking, Retention และการควบคุมข้อมูลส่วนบุคคล |  |
| NFR-23 | Reliability | Transaction Integrity | การ Import, Assignment, Approval และ Status Change ต้องไม่สร้างข้อมูลซ้ำหรือข้อมูลไม่ครบถ้วน |  |
| NFR-24 | Integration | Integration Reliability | Integration ต้องมี Timeout, Retry, Idempotency และ Error Queue |  |
| NFR-25 | Observability | Application Logging | ระบบต้องบันทึก Application Log, Integration Log, Security Log และ Error Log ในรูปแบบที่ค้นหาได้ |  |
| NFR-26 | Observability | Monitoring and Alerting | ระบบต้องตรวจสอบ Availability, Error Rate, Latency, Queue, Storage และ Integration Status |  |
| NFR-27 | Backup | Backup and Restore | Database, Configuration และ Evidence ต้องได้รับการสำรองและสามารถกู้คืนได้ |  |
| NFR-28 | Disaster Recovery | Recovery Time Objective | ระบบต้องสามารถกลับมาให้บริการหลังเหตุขัดข้องรุนแรงภายในเวลาที่กำหนด |  |
| NFR-29 | Disaster Recovery | Recovery Point Objective | ปริมาณข้อมูลสูญหายสูงสุดต้องไม่เกินระยะเวลาที่กำหนด |  |
| NFR-30 | Maintainability | Modular Architecture | ระบบต้องแยก Component ตามหน้าที่ เช่น API, Import Worker, Notification และ Reporting |  |
| NFR-31 | Maintainability | Configuration-driven Design | Risk Weight, SLA, Workflow, Notification และ Approval Matrix ต้องเปลี่ยนผ่าน Configuration ได้ |  |
| NFR-32 | Compatibility | Browser Support | ระบบต้องรองรับ Browser ที่องค์กรใช้งาน |  |
| NFR-33 | Usability | Responsive Design | หน้าจอต้องใช้งานผ่าน Desktop, Tablet และ Mobile Browser ได้ |  |
| NFR-34 | Accessibility | User Interface Accessibility | UI ต้องรองรับ Keyboard Navigation, Label และ Contrast ที่เหมาะสม |  |
| NFR-35 | Data Retention | Retention and Archiving | ระบบต้องเก็บและ Archive ข้อมูลตามประเภทข้อมูลและนโยบายองค์กร |  |
| NFR-36 | Secure SDLC | Secure Development | Source Code ต้องผ่าน Code Review, SAST, SCA, DAST และ Vulnerability Assessment ตามขั้นตอน Secure SDLC |  |
| NFR-37 | Vulnerability Management | Platform Vulnerability Management | Component ของระบบต้องได้รับการตรวจช่องโหว่และ Patch ตาม SLA ขององค์กร |  |
| NFR-38 | Time | Time Synchronization | Server, Database, Log และ Integration ต้องใช้เวลาจากแหล่งอ้างอิงที่องค์กรอนุมัติ |  |
| NFR-39 | Localization | Language and Date Format | ระบบต้องรองรับภาษาและรูปแบบวันเวลาที่องค์กรกำหนด |  |
| NFR-40 | Supportability | Error Handling | ข้อผิดพลาดต้องแสดงข้อความที่ผู้ใช้เข้าใจได้ โดยไม่เปิดเผย Stack Trace, Secret หรือข้อมูลภายใน |  |
| EXCEPTION CASES |  |  |  |  |
| EX# | Topic | Case Name | Case Detail | Note |
| EX-01 | Authentication | SSO Authentication Failure | ระบบไม่สามารถยืนยันตัวตนผ่าน Microsoft Entra ID หรือ Active Directory ได้ |  |
| EX-02 | Authentication | Identity Provider Unavailable | Identity Provider ไม่ตอบสนองหรือไม่สามารถเชื่อมต่อได้ |  |
| EX-03 | Authentication | MFA Failure | ผู้ใช้งานไม่ผ่าน Multi-factor Authentication |  |
| EX-04 | Authentication | Expired or Disabled Account | บัญชีผู้ใช้หมดอายุ ถูกระงับ หรือถูกปิดใช้งาน |  |
| EX-05 | Authentication | Invalid or Expired Session | Session หรือ Access Token หมดอายุ ไม่ถูกต้อง หรือถูกเพิกถอน |  |
| EX-06 | Authorization | Insufficient Permission | ผู้ใช้พยายามใช้ Function ที่ไม่ได้รับอนุญาต |  |
| EX-07 | Authorization | Unauthorized Record Access | ผู้ใช้พยายามเปิด Finding หรือ Asset นอก Data Scope |  |
| EX-08 | Authorization | Self-approval Attempt | ผู้ขอพยายามอนุมัติ Risk Acceptance, False Positive หรือ Exception ของตนเอง |  |
| EX-09 | Authorization | Privileged Role Conflict | ผู้ใช้ได้รับ Role ที่ขัดแย้งกันตาม Segregation of Duties |  |
| EX-10 | Authorization | Unauthorized Export Attempt | ผู้ใช้พยายาม Export ข้อมูลโดยไม่มีสิทธิ์หรือเกิน Data Scope |  |
| EX-11 | Data Import | Unsupported File Format | ผู้ใช้นำเข้าไฟล์ที่ไม่ใช่รูปแบบที่ระบบรองรับ |  |
| EX-12 | Data Import | File Size Exceeded | ไฟล์มีขนาดเกินค่าที่กำหนด |  |
| EX-13 | Data Import | Empty Import File | ไฟล์นำเข้าไม่มีรายการข้อมูล |  |
| EX-14 | Data Import | Invalid File Structure | ชื่อ Column, Data Type หรือโครงสร้างไฟล์ไม่ตรงกับ Template |  |
| EX-15 | Data Import | Missing Mandatory Field | Record ไม่มีข้อมูลบังคับ เช่น Asset, Source, Finding Name หรือ Severity |  |
| EX-16 | Data Import | Invalid Data Type | ค่าข้อมูลไม่ตรงกับประเภท เช่น วันที่ คะแนน หรือตัวระบุ |  |
| EX-17 | Data Import | Duplicate Import Batch | มีการส่งไฟล์หรือ Transaction ID เดิมเข้าสู่ระบบซ้ำ |  |
| EX-18 | Data Import | Partial Batch Import | ข้อมูลบางรายการสำเร็จและบางรายการล้มเหลว |  |
| EX-19 | Data Import | Import Processing Timeout | Batch ใช้เวลาประมวลผลเกินเวลาที่กำหนด |  |
| EX-20 | Data Import | Import Worker Failure | Worker หยุดทำงานระหว่างการนำเข้า |  |
| EX-21 | File Security | Malware Detected in Upload | ตรวจพบ Malware ในไฟล์นำเข้าหรือ Evidence |  |
| EX-22 | File Security | Password-protected File | ไฟล์ถูกเข้ารหัสหรือป้องกันด้วยรหัสผ่านจนระบบตรวจสอบไม่ได้ |  |
| EX-23 | Asset Mapping | Asset Not Found | Finding ไม่สามารถเชื่อมโยงกับ Asset ใน CMDB หรือ Asset Master |  |
| EX-24 | Asset Mapping | Multiple Asset Matches | Finding สามารถ Mapping ได้กับ Asset มากกว่าหนึ่งรายการ |  |
| EX-25 | Asset Mapping | Asset Owner Not Found | Asset ไม่มี Asset Owner หรือ System Owner |  |
| EX-26 | Asset Mapping | Invalid Asset Identifier | Asset Identifier ไม่เป็นไปตามรูปแบบหรือไม่ตรงกับ Source System |  |
| EX-27 | Asset Mapping | Decommissioned Asset | Finding เชื่อมโยงกับ Asset ที่มีสถานะ Decommissioned |  |
| EX-28 | Asset Data | Missing Asset Criticality | Asset ไม่มี Criticality ที่จำเป็นต่อ Risk Calculation |  |
| EX-29 | Asset Data | Invalid Ownership Change | CMDB ส่ง Owner ใหม่แต่ข้อมูลไม่ครบหรือ Role ไม่ถูกต้อง |  |
| EX-30 | CMDB Integration | CMDB Synchronization Failure | ระบบไม่สามารถรับข้อมูล Asset จาก CMDB ได้ |  |
| EX-31 | CMDB Integration | CMDB Record Deleted | Asset ที่เคยมีอยู่ไม่ปรากฏในข้อมูลต้นทางรอบล่าสุด |  |
| EX-32 | Scanner Integration | Scanner API Unavailable | Scanner API ไม่พร้อมให้บริการ |  |
| EX-33 | Scanner Integration | Scanner API Timeout | Scanner ไม่ตอบสนองภายในเวลาที่กำหนด |  |
| EX-34 | Scanner Integration | Invalid API Credential | Credential หรือ Token สำหรับ Scanner ไม่ถูกต้องหรือหมดอายุ |  |
| EX-35 | Scanner Integration | Scanner Rate Limit Exceeded | จำนวน Request เกินข้อจำกัดของ Scanner |  |
| EX-36 | Scanner Integration | Incomplete Scan Result | Scan Result ไม่มีหน้าข้อมูลครบถ้วนหรือ Pagination ขาดหาย |  |
| EX-37 | Scanner Integration | Unsupported Scanner Schema | Scanner เปลี่ยน Data Schema โดยระบบยังไม่รองรับ |  |
| EX-38 | Scanner Integration | Duplicate Scan Result | Scanner ส่ง Scan Result หรือ Finding เดิมซ้ำ |  |
| EX-39 | ITSM Integration | Ticket Creation Failure | ไม่สามารถสร้าง Ticket ใน ITSM ได้ |  |
| EX-40 | ITSM Integration | Ticket Status Conflict | สถานะ Ticket ใน ITSM ขัดแย้งกับสถานะในระบบ |  |
| EX-41 | ITSM Integration | Duplicate Ticket Created | พบ Ticket มากกว่าหนึ่งใบสำหรับ Remediation Task เดียว |  |
| EX-42 | Notification | Email Delivery Failure | Email Server ปฏิเสธหรือส่งข้อความไม่สำเร็จ |  |
| EX-43 | Notification | Microsoft Teams Delivery Failure | ไม่สามารถส่ง Notification ไปยัง Microsoft Teams ได้ |  |
| EX-44 | SIEM Integration | Security Event Delivery Failure | ระบบไม่สามารถส่ง Security Event ไปยัง SIEM ได้ |  |
| EX-45 | Deduplication | Ambiguous Duplicate Match | Finding มีความคล้ายคลึงหลายรายการแต่ไม่สามารถตัดสินได้ว่าเป็นรายการซ้ำ |  |
| EX-46 | Deduplication | Incorrect Merge Detected | ผู้ใช้พบว่ารายการถูก Merge ผิด |  |
| EX-47 | Reference Data | CVE Not Found | CVE จาก Scanner ไม่มีอยู่ใน Reference Data |  |
| EX-48 | Reference Data | Unsupported CVSS Version | Scanner ส่ง CVSS Version ที่ระบบไม่รองรับ |  |
| EX-49 | Reference Data | Invalid CVSS Vector | CVSS Vector ไม่เป็นไปตามรูปแบบมาตรฐาน |  |
| EX-50 | Risk Calculation | Risk Calculation Failure | ระบบไม่สามารถคำนวณ Risk Score เนื่องจากข้อมูลไม่ครบหรือ Formula Error |  |
| EX-51 | Risk Calculation | Risk Score Out of Range | คะแนนที่คำนวณได้ต่ำกว่า 0 หรือสูงกว่า 100 |  |
| EX-52 | Risk Calculation | Missing Threat Intelligence | ไม่สามารถรับข้อมูล KEV หรือ Threat Intelligence ล่าสุดได้ |  |
| EX-53 | Risk Calculation | Conflicting Severity | Scanner Severity, CVSS Severity และ Risk Level ไม่ตรงกัน |  |
| EX-54 | Risk Configuration | Invalid Risk Weight | ผลรวม Weight ไม่ตรงกับเงื่อนไขที่กำหนด หรือมีค่าติดลบ |  |
| EX-55 | Workflow | Invalid Status Transition | ผู้ใช้พยายามเปลี่ยนไปยังสถานะที่ Workflow ไม่อนุญาต |  |
| EX-56 | Workflow | Missing Required Evidence | ผู้ใช้ส่งรายการเข้า Pending Verification หรือ Closed โดยไม่มีหลักฐานครบถ้วน |  |
| EX-57 | Workflow | Concurrent Update Conflict | ผู้ใช้สองคนแก้ไข Finding เดียวกันพร้อมกัน |  |
| EX-58 | Workflow | Workflow Configuration Changed | Workflow ถูกเปลี่ยนขณะที่มีรายการกำลังดำเนินการ |  |
| EX-59 | Assignment | Assignment Rule Failure | ระบบไม่สามารถหาผู้รับผิดชอบจาก Assignment Rule |  |
| EX-60 | Assignment | Inactive Assignee | ผู้รับผิดชอบถูกปิดบัญชี ลาออก หรือไม่มีสิทธิ์ใน Scope |  |
| EX-61 | Assignment | Invalid Bulk Assignment | บางรายการในการมอบหมายแบบกลุ่มอยู่นอกสิทธิ์หรือไม่ผ่าน Validation |  |
| EX-62 | Assignment | Cross-organization Assignment | ผู้ใช้พยายามมอบหมายงานไปยังบุคคลนอกหน่วยงานโดยไม่มีสิทธิ์ |  |
| EX-63 | Remediation | Target Date Exceeds SLA | ผู้ใช้กำหนด Target Date เกิน SLA โดยไม่มี Extension |  |
| EX-64 | Remediation | ITSM Task Closed Without Verification | ITSM Ticket ถูกปิด แต่ Finding ยังไม่ผ่าน Verification |  |
| EX-65 | SLA | SLA Calculation Failure | ระบบไม่สามารถคำนวณ Due Date เนื่องจาก Policy หรือ Calendar ไม่สมบูรณ์ |  |
| EX-66 | SLA | No Applicable SLA Policy | ไม่พบ SLA Policy ที่ตรงกับ Risk Level หรือ Asset Type |  |
| EX-67 | SLA | Invalid Business Calendar | Working Day หรือ Holiday Calendar มีข้อมูลผิดหรือทับซ้อน |  |
| EX-68 | SLA | SLA Recalculation Conflict | การเปลี่ยน Risk Level ทำให้ Due Date ใหม่ย้อนหลังหรือผ่านไปแล้ว |  |
| EX-69 | SLA | Unauthorized SLA Pause | ผู้ใช้พยายามหยุด SLA โดยไม่มีเหตุผลหรือสิทธิ์ |  |
| EX-70 | Escalation | Escalation Recipient Missing | ไม่พบ Manager, Asset Owner หรือ Risk Owner สำหรับรับ Escalation |  |
| EX-71 | Notification | Duplicate Notification Storm | Event เดียวสร้าง Notification ซ้ำจำนวนมาก |  |
| EX-72 | Notification | Invalid Notification Template | Template มีตัวแปรไม่ถูกต้องหรือไม่สามารถ Render ได้ |  |
| EX-73 | Notification | Invalid Recipient Address | Email หรือ Teams Identifier ของผู้รับไม่ถูกต้อง |  |
| EX-74 | Verification | Rescan Request Failure | ไม่สามารถส่งคำขอ Rescan ไปยัง Scanner |  |
| EX-75 | Verification | Rescan Target Not Found | Scanner ไม่พบ Asset หรือ Target ที่ร้องขอตรวจซ้ำ |  |
| EX-76 | Verification | Inconclusive Rescan | ผล Rescan ไม่สามารถยืนยันว่าแก้ไขแล้วหรือยัง |  |
| EX-77 | Verification | Vulnerability Still Detected | ผล Rescan ยังคงตรวจพบช่องโหว่ |  |
| EX-78 | Verification | Partial Remediation | แก้ไขบาง Instance แต่ยังเหลือบางรายการ |  |
| EX-79 | Closure | Closure Approval Timeout | Approver ไม่ดำเนินการภายใน Approval SLA |  |
| EX-80 | Closure | Closure Without Independent Verification | Critical Finding ถูกส่งขอปิดโดยไม่มี Independent Verification |  |
| EX-81 | Closure | Finding Reappears After Closure | ช่องโหว่เดิมถูกตรวจพบภายหลังการปิด |  |
| EX-82 | Risk Acceptance | Missing Business Justification | คำขอไม่มีเหตุผลทางธุรกิจหรือผลกระทบที่ชัดเจน |  |
| EX-83 | Risk Acceptance | Expiry Date Exceeds Maximum | วันที่สิ้นสุดเกินระยะเวลาสูงสุดตาม Risk Level |  |
| EX-84 | Risk Acceptance | Missing Compensating Control | คำขอระดับที่กำหนดไม่มีมาตรการทดแทน |  |
| EX-85 | Risk Acceptance | Risk Acceptance Expired | การยอมรับความเสี่ยงครบกำหนดโดยไม่มีการต่ออายุ |  |
| EX-86 | Risk Acceptance | Approver Unavailable | ผู้อนุมัติหลักถูกปิดบัญชี ลางาน หรือไม่สามารถดำเนินการ |  |
| EX-87 | False Positive | Insufficient Technical Evidence | คำขอ False Positive ไม่มีหลักฐานทางเทคนิคเพียงพอ |  |
| EX-88 | False Positive | False Positive Expired | สถานะ False Positive ถึงวันทบทวนหรือหมดอายุ |  |
| EX-89 | Exception | SLA Extension Submitted Late | ยื่นคำขอหลัง SLA Breach โดยไม่มีเหตุฉุกเฉิน |  |
| EX-90 | Exception | Maximum Extension Exceeded | ระยะเวลาหรือจำนวนครั้งของ SLA Extension เกิน Policy |  |
| EX-91 | Compensating Control | Control Expired | Compensating Control หมดอายุหรือไม่ผ่านการทบทวน |  |
| EX-92 | Reporting | Report Generation Failure | ระบบไม่สามารถสร้างรายงานได้ |  |
| EX-93 | Reporting | Report Query Timeout | รายงานใช้เวลาประมวลผลเกินกำหนด |  |
| EX-94 | Reporting | Report Data Scope Violation | รายงานมีข้อมูลนอก Scope ของผู้ร้องขอ |  |
| EX-95 | Export | Export File Generation Failure | ไม่สามารถสร้างหรือจัดเก็บไฟล์ Export ได้ |  |
| EX-96 | Export | Export Record Limit Exceeded | จำนวนรายการเกินขีดจำกัดที่กำหนด |  |
| EX-97 | Export | Export Link Expired | ผู้ใช้เปิด Download Link หลังหมดอายุ |  |
| EX-98 | Audit | Audit Log Write Failure | ระบบไม่สามารถเขียน Audit Log สำหรับกิจกรรมควบคุมได้ |  |
| EX-99 | Audit | Audit Storage Unavailable | พื้นที่หรือบริการจัดเก็บ Audit Log ไม่พร้อมใช้งาน |  |
| EX-100 | Audit | Audit Log Integrity Failure | ตรวจพบ Audit Record ถูกแก้ไข สูญหาย หรือไม่ผ่าน Integrity Check |  |
| EX-101 | Database | Database Connection Failure | Application ไม่สามารถเชื่อมต่อ Database ได้ |  |
| EX-102 | Database | Transaction Deadlock | เกิด Deadlock ระหว่าง Transaction |  |
| EX-103 | Database | Data Integrity Constraint Violation | การบันทึกข้อมูลขัดกับ Unique Key หรือ Referential Integrity |  |
| EX-104 | Database | Replication Lag | Read Replica มีข้อมูลล่าช้าเกิน Threshold |  |
| EX-105 | Message Queue | Queue Unavailable | Message Queue ไม่พร้อมใช้งาน |  |
| EX-106 | Message Queue | Poison Message | Message เดิมประมวลผลล้มเหลวซ้ำเกินจำนวนที่กำหนด |  |
| EX-107 | Storage | Storage Capacity Threshold Exceeded | Database หรือ Object Storage ใช้พื้นที่เกิน Threshold |  |
| EX-108 | Storage | Evidence Object Missing | Metadata มีอยู่แต่ไฟล์ Evidence ไม่พบใน Storage |  |
| EX-109 | Cache | Cache Service Failure | Cache ไม่พร้อมใช้งาน |  |
| EX-110 | API Gateway | Rate Limit Exceeded | Client เรียก API เกิน Rate Limit |  |
| EX-111 | Certificate | Integration Certificate Expired | Certificate ที่ใช้เชื่อมต่อระบบภายนอกหมดอายุ |  |
| EX-112 | Encryption | Encryption Key Unavailable | ระบบไม่สามารถเข้าถึง Key Management Service |  |
| EX-113 | Time | Time Synchronization Failure | เวลา Server หรือ Component แตกต่างเกิน Threshold |  |
| EX-114 | DR | Disaster Recovery Failover Failure | การสลับไป DR Site ไม่สำเร็จ |  |
| BUSSINESS RULE |  |  |  |  |
| EX# | Topic | Rule Name | Rule Detail | Note |
| BR-01 | Risk Classification | Risk Level Classification | Risk Level ต้องแบ่งเป็น Critical, High, Medium, Low และ Informational ตาม Risk Score และ Rule ที่องค์กรอนุมัติ |  |
| BR-02 | Risk Prioritization | Risk-based Prioritization | การจัดลำดับต้องพิจารณาทั้ง Technical Severity และ Business Context ไม่ใช้ CVSS เพียงปัจจัยเดียว |  |
| BR-03 | KEV | Known Exploited Vulnerability | Finding ที่ตรงกับ KEV ต้องได้รับ Priority สูงกว่ารายการทั่วไป และเข้าสู่ Escalation ตาม Policy |  |
| BR-04 | Exposure | Internet-facing Priority | ช่องโหว่ High หรือ Critical บน Internet-facing Asset ต้องได้รับการเร่งรัดและแจ้งเตือนตาม Escalation Matrix |  |
| BR-05 | Asset Criticality | Mandatory Criticality | Asset ที่อยู่ใน Production Scope ต้องมี Asset Criticality ก่อนคำนวณ SLA ขั้นสุดท้าย |  |
| BR-06 | Ownership | Mandatory Owner | Finding ต้องมี Asset Owner หรือ Remediation Owner ก่อนเปลี่ยนเป็น Assigned |  |
| BR-07 | Assignment | Assignment by Responsibility | การมอบหมายต้องอ้างอิง Asset Ownership, Business Unit, Technology หรือ Assignment Rule ที่ได้รับอนุมัติ |  |
| BR-08 | SLA | SLA by Risk Level | SLA ต้องกำหนดตาม Risk Level และสามารถปรับลดตาม Asset Criticality, Internet Exposure และ KEV |  |
| BR-09 | SLA | SLA Start Time | SLA เริ่มนับเมื่อ Finding ผ่าน Validation และมีข้อมูล Asset เพียงพอ หรือเมื่อถึงสถานะที่ Policy กำหนด |  |
| BR-10 | SLA | SLA Recalculation | หาก Risk Level สูงขึ้น ระบบต้องใช้ Due Date ที่เข้มงวดกว่า หาก Risk Level ลดลง การขยายเวลาอาจต้องผ่าน Approval |  |
| BR-11 | Escalation | Overdue Escalation | รายการที่เกิน SLA ต้องถูก Escalate ตามจำนวนวันที่เกินและ Risk Level |  |
| BR-12 | Duplicate | Duplicate Handling | Finding ที่มี Asset, Vulnerability Identifier และ Location เดียวกันตาม Duplicate Rule ต้องเชื่อมกับ Instance เดิม |  |
| BR-13 | Recurrence | Recurring Vulnerability | Finding ที่เคย Closed แล้วแต่ถูกตรวจพบใหม่ต้องเปลี่ยนเป็น Reopened หรือสร้าง Recurrence Record ตาม Rule |  |
| BR-14 | Closure | Mandatory Evidence | Finding ต้องมีหลักฐานการแก้ไขก่อนส่งเข้า Pending Verification |  |
| BR-15 | Closure | Mandatory Verification | Finding ต้องผ่าน Verification ก่อนเปลี่ยนเป็น Closed |  |
| BR-16 | Closure | Critical Independent Verification | Critical Finding ต้องได้รับ Independent Verification จาก Security Analyst หรือผู้ตรวจสอบที่ไม่ได้เป็นผู้แก้ไข |  |
| BR-17 | Closure | Rescan Before Closure | Finding ที่มี Scanner Integration ต้องใช้ผล Rescan เป็นหลักฐานก่อนปิด เว้นแต่ได้รับอนุมัติให้ใช้ Manual Verification |  |
| BR-18 | False Positive | False Positive Evidence | คำขอ False Positive ต้องมี Technical Justification, Evidence และ Review Date |  |
| BR-19 | False Positive | False Positive Expiry | False Positive ต้องมีวันหมดอายุหรือวันทบทวน เมื่อครบกำหนดระบบต้องส่งกลับมาตรวจสอบใหม่ |  |
| BR-20 | Risk Acceptance | Risk Acceptance Conditions | Risk Acceptance ต้องระบุ Business Justification, Residual Risk, Compensating Control, Owner และ Expiry Date |  |
| BR-21 | Risk Acceptance | Maximum Acceptance Period | ระยะเวลายอมรับความเสี่ยงต้องไม่เกินค่าสูงสุดที่กำหนดตาม Risk Level |  |
| BR-22 | Risk Acceptance | Acceptance Expiration | เมื่อ Risk Acceptance หมดอายุและไม่ได้รับการต่ออายุ Finding ต้องกลับสู่สถานะที่ต้องดำเนินการ |  |
| BR-23 | Risk Acceptance | Critical Approval Matrix | Critical Risk Acceptance ต้องได้รับอนุมัติจาก Risk Owner และผู้บริหารตามระดับที่องค์กรกำหนด |  |
| BR-24 | Exception | SLA Extension Condition | SLA Extension ต้องมีเหตุผล แผนแก้ไข Target Date และ Compensating Control |  |
| BR-25 | Exception | Maximum SLA Extension | การขยาย SLA แต่ละครั้งและจำนวนครั้งสูงสุดต้องเป็นไปตาม Risk Level และ Policy |  |
| BR-26 | Compensating Control | Control Effectiveness Review | Compensating Control ต้องมี Owner, Evidence, Effective Period และ Review Date |  |
| BR-27 | Workflow | Valid Status Transition | ผู้ใช้สามารถเปลี่ยนสถานะได้เฉพาะ Transition ที่ Workflow อนุญาต |  |
| BR-28 | Approval | No Self-approval | ผู้ยื่นคำขอไม่สามารถอนุมัติ Risk Acceptance, False Positive, SLA Extension หรือ Security Exception ของตนเอง |  |
| BR-29 | Approval | Approval Decision | Approver ต้องระบุ Comment เมื่อ Reject, Return หรืออนุมัติรายการ Critical |  |
| BR-30 | Data Access | Organizational Segregation | ผู้ใช้เข้าถึงได้เฉพาะ Finding และ Asset ตาม Business Unit หรือ Responsibility Scope ยกเว้น Role ที่ได้รับสิทธิ์ระดับองค์กร |  |
| BR-31 | Export | Export Authorization | การ Export รายละเอียดช่องโหว่ต้องจำกัดตาม Role, Data Scope และ Data Classification |  |
| BR-32 | Sensitive Data | Data Masking | Credential, Token, Personal Data และข้อมูล Sensitive ต้องถูก Mask หรือซ่อนจาก Role ที่ไม่จำเป็น |  |
| BR-33 | Audit | Mandatory Audit Events | การดู แก้ไข มอบหมาย เปลี่ยนสถานะ อนุมัติ Export และตั้งค่าระบบต้องสร้าง Audit Log |  |
| BR-34 | Data Retention | Retention Policy | Vulnerability, Evidence, Approval, Integration Log และ Audit Log ต้องเก็บตาม Retention Policy ของแต่ละประเภทข้อมูล |  |
| BR-35 | Integration | Source Traceability | Finding ทุกข้อต้องระบุ Source System, Source Record ID, Import Batch และ First/Last Detected Date |  |
| BR-36 | Integration | Idempotent Processing | การนำเข้าหรือส่งข้อมูลซ้ำด้วย Transaction ID เดิมต้องไม่สร้างรายการซ้ำ |  |
| BR-37 | Integration | Failed Integration Handling | Integration ที่ล้มเหลวต้องถูก Retry ตาม Policy และส่งเข้าคิวตรวจสอบเมื่อเกินจำนวนครั้งสูงสุด |  |
| BR-38 | Asset Lifecycle | Decommissioned Asset | Finding ของ Asset ที่ Decommissioned ต้องไม่ถูกลบอัตโนมัติ แต่ต้องเข้าสู่กระบวนการตรวจสอบและ Archive |  |
| BR-39 | Emergency | Emergency Remediation | Critical Finding ที่มี Active Exploitation อาจเข้าสู่ Emergency Remediation และ Emergency Change Process |  |
| BR-40 | Configuration | Controlled Configuration Change | การแก้ Risk Model, SLA, Workflow, Approval Matrix และ Integration Configuration ต้องผ่านสิทธิ์และ Change Control |  |
| BR-41 | Reporting | KPI Consistency | KPI และรายงานต้องใช้ Definition และ Formula เดียวกันในทุก Dashboard |  |
| BR-42 | Evidence | Evidence Retention | Evidence ต้องเชื่อมโยงกับ Finding, Task, Request และผู้ Upload และต้องไม่ถูกแทนที่โดยไม่มี Version History |  |
| BR-43 | User Access | Inactive User Handling | เมื่อผู้ใช้ถูกปิดการใช้งาน งานที่ยังเปิดต้องถูกส่งให้ Owner สำรองหรือ Queue ที่กำหนด |  |
| BR-44 | External User | External Account Expiry | External Assessor Account ต้องมี Expiry Date และเข้าถึงเฉพาะ Project ที่ได้รับมอบหมาย |  |
| BR-45 | Data Deletion | No Direct Finding Deletion | Finding ที่เข้าสู่ Workflow แล้วต้องไม่ถูกลบโดยผู้ใช้งานทั่วไป แต่ใช้สถานะ Invalid, Duplicate หรือ Archived ตาม Rule |  |
| UI DESIGN |  |  |  |  |
| PAGE | PICTURE |  |  |  |
| - | - |  |  |  |
