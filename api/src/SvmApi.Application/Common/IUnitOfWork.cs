namespace SvmApi.Application.Common;

/// <summary>
/// ขอบเขต transaction เดียวกันสำหรับงานที่ต้อง commit พร้อมกัน (เช่น session mutation + audit row).
/// รองรับ execution strategy (retry) และ "join" transaction ที่เปิดอยู่แล้ว (ไม่ซ้อน).
/// ภายใน <paramref name="work"/> เรียก SaveChanges ได้ตามปกติ — commit เกิดเมื่อ work สำเร็จทั้งก้อน.
/// เพราะ work อาจถูก retry ทั้งก้อน ต้อง idempotent ต่อสถานะนอก DbContext.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
