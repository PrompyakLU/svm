import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guard';

/** Core slice routing (Wave 2). Views โหลดแบบ lazy (code-split ต่อ feature). */
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login').then(m => m.Login),
    title: 'เข้าสู่ระบบ · SVM',
  },
  { path: '', canActivateChild: [authGuard], children: [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    data: { permissions: ['dashboard.ops', 'dashboard.exec'] },
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
    title: 'แดชบอร์ด · SVM',
  },
  {
    path: 'vulnerabilities',
    data: { permissions: ['instance.read'] },
    loadComponent: () => import('./features/vulnerabilities/vulnerability-list').then((m) => m.VulnerabilityList),
    title: 'ช่องโหว่ · SVM',
  },
  {
    path: 'vulnerabilities/:id',
    data: { permissions: ['instance.read'] },
    loadComponent: () => import('./features/vulnerabilities/vulnerability-detail').then((m) => m.VulnerabilityDetail),
    title: 'รายละเอียดช่องโหว่ · SVM',
  },
  {
    path: 'intake',
    data: { permissions: ['finding.import', 'finding.create', 'finding.dedup'] },
    loadComponent: () => import('./features/intake/intake').then((m) => m.Intake),
    title: 'นำเข้า · SVM',
  },
  {
    path: 'remediation',
    data: { permissions: ['instance.read', 'task.read'] },
    loadComponent: () => import('./features/remediation/remediation').then((m) => m.Remediation),
    title: 'การแก้ไข · SVM',
  },
  {
    path: 'verification',
    data: { permissions: ['verification.submit', 'verification.rescan', 'verification.record'] },
    loadComponent: () => import('./features/verification/verification').then((m) => m.Verification),
    title: 'ตรวจสอบ · SVM',
  },
  {
    path: 'admin',
    data: { permissions: ['admin.masterdata', 'admin.workflow', 'admin.sla', 'admin.risk', 'admin.assignment', 'admin.users'] },
    loadComponent: () => import('./features/admin/admin').then((m) => m.Admin),
    title: 'ผู้ดูแลระบบ · SVM',
  },
  {
    path: 'audit',
    data: { permissions: ['audit.read'] },
    loadComponent: () => import('./features/audit/audit').then((m) => m.Audit),
    title: 'บันทึกตรวจสอบ · SVM',
  },
  { path: '**', redirectTo: 'dashboard' },
  ] },
];
