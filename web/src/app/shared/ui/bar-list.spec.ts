import { TestBed } from '@angular/core/testing';
import { BarList } from './bar-list';

describe('BarList missing data', () => {
  it('distinguishes an unavailable dataset from a supplied zero count', async () => {
    await TestBed.configureTestingModule({ imports: [BarList] }).compileComponents();
    const fixture = TestBed.createComponent(BarList);
    fixture.componentRef.setInput('data', []);
    fixture.componentRef.setInput('emptyMessage', 'ยังไม่มีข้อมูลสถานะงาน');
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('ยังไม่มีข้อมูลสถานะงาน');
    expect(root.querySelector('ul')).toBeNull();
    fixture.componentRef.setInput('data', [{ key: 'New', count: 0 }]);
    fixture.componentRef.setInput('labelMap', { New: 'ใหม่' });
    fixture.detectChanges();
    expect(root.querySelector('.empty-data')).toBeNull();
    expect(root.querySelector('.bar__label')?.textContent).toBe('ใหม่');
    expect(root.querySelector('.bar__value')?.textContent).toBe('0');
  });
});
