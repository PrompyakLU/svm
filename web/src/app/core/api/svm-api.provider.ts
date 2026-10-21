import { Provider } from '@angular/core';
import { environment } from '../../../environments/environment';
import { SvmApi } from './svm-api';
import { MockSvmApi } from './mock-svm-api';
import { HttpSvmApi } from './http-svm-api';

/**
 * เลือก implementation ของ SvmApi ตาม environment.useMock.
 * Wave 6: เพิ่ม HttpSvmApi แล้ว return provider นั้นเมื่อ useMock=false.
 */
export function provideSvmApi(): Provider {
  if (environment.authMode === 'demo' && environment.useMock) {
    return { provide: SvmApi, useClass: MockSvmApi };
  }
  return { provide: SvmApi, useClass: HttpSvmApi };
}
