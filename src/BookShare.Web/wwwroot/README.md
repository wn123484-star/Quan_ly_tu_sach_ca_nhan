# Sprint 2 Deliverables - Lê Hoàn Lâm (BIT210210)

Issue #8: `[Sprint2] Lâm - Chuẩn hóa CSDL Quan hệ, Tối ưu Truy vấn Tìm kiếm/Phân trang & Dữ liệu Kiểm thử`

Nhánh làm việc: `sprint2/lam-database-query`

## Cấu trúc deliverable

```
download/
├── database/
│   ├── 01_BookShare_Schema.sql                 (654 dòng) - 27 bảng + 9 index tối ưu
│   ├── 02_BookShare_SeedData.sql               (572 dòng) - 4 users, 30 books, 50 copies, full history
│   ├── 03_BookShare_SearchFilterPagination.sql (378 dòng) - 5 SP Search/Filter/Pagination
│   ├── 04_BookShare_DashboardStats.sql         (378 dòng) - 9 SP thống kê Dashboard
│   ├── 05_BookShare_Views.sql                  (316 dòng) - 7 Views tổng hợp
│   └── 06_BookShare_Functions.sql               (296 dòng) - 7 Functions (TVF + Scalar)
├── erd/
│   └── ERD_BookShare.png                        - Sơ đồ ERD 24 bảng + 3 Identity
├── src/BookShare.Web/
│   ├── Models/
│   │   └── PagedResult.cs                       (183 dòng) - Generic PagedResult<T>
│   └── Services/
│       ├── S06SearchDiscoveryService.cs         (239 dòng) - Search + Pagination S06
│       ├── S07LibraryCatalogService.cs          (308 dòng) - Library Catalog S07
│       └── S22AdminAnalyticsService.cs          (526 dòng) - Dashboard Analytics S22 + S03
├── Bao_Cao_Sprint2_Lam_Bit250210.docx           (615 KB) - Báo cáo 29 trang
├── Bao_Cao_Sprint2_Lam_Bit250210.pdf            (553 KB) - Bản PDF để xem trước
└── README.md                                    (file này)
```

## Thứ tự chạy SQL trên SQL Server 2022

```bash
# 1. Tạo schema (bảng + index)
sqlcmd -S localhost -U sa -P "YourPassword123!" -i database/01_BookShare_Schema.sql

# 2. Nạp dữ liệu mẫu
sqlcmd -S localhost -U sa -P "YourPassword123!" -i database/02_BookShare_SeedData.sql

# 3. Stored procedures (chạy theo thứ tự)
sqlcmd -S localhost -U sa -P "YourPassword123!" -i database/03_BookShare_SearchFilterPagination.sql
sqlcmd -S localhost -U sa -P "YourPassword123!" -i database/04_BookShare_DashboardStats.sql
sqlcmd -S localhost -U sa -P "YourPassword123!" -i database/05_BookShare_Views.sql
sqlcmd -S localhost -U sa -P "YourPassword123!" -i database/06_BookShare_Functions.sql
```

## Tài khoản mẫu (sau khi nạp seed data)

| Vai trò    | Email                       | Password        | Mục đích                |
|------------|-----------------------------|-----------------|-------------------------|
| Admin      | admin@cmcuni.edu.vn         | BookShare@2026  | Quản trị hệ thống       |
| Librarian  | librarian@cmcuni.edu.vn     | BookShare@2026  | Thủ thư ĐH CMC          |
| Curator    | curator@cmcuni.edu.vn       | BookShare@2026  | Curator CLB đọc sách    |
| User       | user04@cmcuni.edu.vn        | BookShare@2026  | Sinh viên mượn sách     |

## Kiểm thử nhanh

```sql
-- Test search
EXEC dbo.sp_SearchBooks @Keyword = N'ASP.NET', @Page = 1, @PageSize = 5;

-- Test filter Library vs P2P
EXEC dbo.sp_SearchBookCopiesPaged @OwnerType = N'System', @Status = N'Available';

-- Test dashboard ratio
EXEC dbo.sp_GetLoanTypeRatio;

-- Test admin overview
EXEC dbo.sp_GetAdminAnalyticsOverview;
```

## Tác giả

- **Họ tên**: Lê Hoàn Lâm
- **MSSV**: BIT250210
- **Lớp**: 25IT - Nhóm 5
- **Môn học**: Lập trình Web (5 tín chỉ)
- **Trường**: Đại học CMC
- **Repository**: https://github.com/wn123484-star/Quan_ly_tu_sach_ca_nhan
- **Issue**: #8
