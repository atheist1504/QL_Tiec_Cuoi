# Sinh file CSV cho MS Project và Jira từ danh sách gói công việc (WBS).
# Chạy: powershell -ExecutionPolicy Bypass -File docs\pm\export-wbs.ps1
# Khi sửa WBS, sửa bảng $tasks bên dưới rồi chạy lại.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Lịch sprint: ngày bắt đầu, ngày kết thúc
$sprints = @{
    0 = @('2026-11-30', '2026-12-06')
    1 = @('2026-12-07', '2026-12-20')
    2 = @('2026-12-21', '2027-01-03')
    3 = @('2027-01-04', '2027-01-17')
    4 = @('2027-01-18', '2027-01-29')
}

# Mã | Tên | Giờ | Phụ trách | Sprint từ | Sprint đến | Đã xong (1/0)
$raw = @'
1|Quản lý dự án||||
1.1|Project Charter|4|Đạt|0|0|1
1.2|WBS và ước lượng|4|Đạt|0|0|1
1.3|Lịch MS Project, đường găng|4|Đức|0|0|0
1.4|Kế hoạch chi phí, EVM giữa và cuối kỳ|6|Đạt|2|4|0
1.5|Sổ rủi ro, cập nhật hằng sprint|4|Đức|1|4|0
1.6|Kế hoạch truyền thông, biên bản họp|6|Đức|0|4|0
1.7|Backlog Jira, báo cáo sprint|6|Đức|0|4|0
1.8|Phân tích làm hay mua (LLM, thư viện PDF)|3|Đạt|2|2|0
2|Phân tích yêu cầu||||
2.1|Đặc tả yêu cầu, user story|6|Đức|0|0|0
2.2|Use case, quy trình nghiệp vụ|6|Đức|1|1|0
2.3|Dữ liệu mẫu: sảnh, thực đơn, dịch vụ, giá|3|Đức|1|1|0
3|Thiết kế||||
3.1|Kiến trúc hệ thống|2|Đạt|0|0|1
3.2|Thiết kế CSDL|4|Đạt|0|0|0
3.3|Thiết kế API|3|Đạt|1|1|0
3.4|Mockup giao diện (Figma)|10|Đức|1|2|0
3.5|Thiết kế chatbot: tool, prompt|3|Đạt|2|2|0
4|Lịch sảnh theo ca||||
4.1|API giữ, hủy sảnh, chống trùng|6|Đạt|1|1|1
4.2|Màn hình lịch sảnh|5|Đạt|1|1|1
5|Thực đơn, dịch vụ, báo giá||||
5.1|API thực đơn, dịch vụ|5|Đạt|1|1|0
5.2|API báo giá|6|Đạt|1|1|0
5.3|Màn hình thực đơn, dịch vụ|5|Đức|1|1|0
5.4|Màn hình báo giá|6|Đức|2|2|0
6|Hợp đồng, đặt cọc||||
6.1|API hợp đồng, đặt cọc|8|Đạt|2|2|0
6.2|Màn hình hợp đồng|6|Đức|2|2|0
6.3|Mẫu và xuất hợp đồng PDF|5|Đức|3|3|0
7|Thanh lý và báo cáo||||
7.1|API thanh lý hợp đồng|5|Đạt|3|3|0
7.2|Màn hình thanh lý|4|Đức|3|3|0
7.3|Báo cáo doanh thu mùa cưới|7|Đạt|3|3|0
8|Chatbot AI||||
8.1|Tích hợp Claude API, tra lịch và báo giá|8|Đạt|3|3|0
8.2|Tool calling: giữ sảnh, tạo báo giá nháp|8|Đạt|4|4|0
8.3|Màn hình chat trong app|4|Đức|3|3|0
9|Nhắc thanh toán (mức Tốt)||||
9.1|Nhắc thanh toán các đợt còn nợ|4|Đạt|4|4|0
10|Kiểm thử||||
10.1|Unit test|6|Đạt|2|4|0
10.2|Test case, kiểm thử tích hợp|8|Đức|2|4|0
10.3|Kiểm thử chấp nhận (UAT)|4|Đức|4|4|0
11|Triển khai||||
11.1|Triển khai API và CSDL lên máy chủ|5|Đạt|2|2|0
11.2|Đóng gói bộ cài Windows|4|Đạt|4|4|0
12|Kết thúc dự án||||
12.1|Hướng dẫn sử dụng|4|Đức|4|4|0
12.2|Slide và kịch bản demo|4|Đức|4|4|0
12.3|Báo cáo tổng kết, bài học kinh nghiệm|4|Đạt, Đức|4|4|0
'@

$tasks = foreach ($line in ($raw -split "`r?`n" | Where-Object { $_.Trim() })) {
    $p = $line.Split('|')
    [pscustomobject]@{
        Code  = $p[0]; Name = $p[1]
        Hours = if ($p[2]) { [int]$p[2] } else { $null }
        Owner = $p[3]
        From  = if ($p[4] -ne $null -and $p[4] -ne '') { [int]$p[4] } else { $null }
        To    = if ($p[5] -ne $null -and $p[5] -ne '') { [int]$p[5] } else { $null }
        Done  = $p.Count -gt 6 -and $p[6] -eq '1'
        Level = $p[0].Split('.').Count
    }
}

function Format-Date([string]$iso) { ([datetime]$iso).ToString('dd/MM/yyyy') }
function Get-SprintLabel($t) { if ($t.From -eq $t.To) { "Sprint $($t.From)" } else { "Sprint $($t.From)-$($t.To)" } }

# Tính giờ, ngày cho các nhánh cấp 1 từ gói con
foreach ($b in $tasks | Where-Object Level -eq 1) {
    $kids = $tasks | Where-Object { $_.Level -eq 2 -and $_.Code.StartsWith("$($b.Code).") }
    $b.Hours = ($kids | Measure-Object Hours -Sum).Sum
    $b.From = [int]($kids | Measure-Object From -Minimum).Minimum
    $b.To = [int]($kids | Measure-Object To -Maximum).Maximum
    $b.Done = -not ($kids | Where-Object { -not $_.Done })
}

$total = ($tasks | Where-Object Level -eq 2 | Measure-Object Hours -Sum).Sum
if ($total -ne 205) { throw "Tổng giờ các gói công việc là $total, khác 205 trong tài liệu WBS." }

# 1) MS Project: nhập bằng File > Open > chọn file CSV > Import Wizard (map theo tên cột)
$tasks | ForEach-Object {
    [pscustomobject]@{
        'WBS'            = $_.Code
        'Outline Level'  = $_.Level
        'Name'           = $_.Name
        'Work'           = "$($_.Hours)h"
        'Resource Names' = if ($_.Level -eq 2) { $_.Owner } else { '' }
        'Start'          = Format-Date $sprints[$_.From][0]
        'Finish'         = Format-Date $sprints[$_.To][1]
        'Text1'          = Get-SprintLabel $_
        '% Complete'     = if ($_.Done) { 100 } else { 0 }
    }
} | Export-Csv (Join-Path $here 'wbs-msproject.csv') -NoTypeInformation -Encoding UTF8

# 2) Jira: Project settings > External system import > CSV.
#    Nhánh cấp 1 = Epic, gói cấp 2 = Task có Parent là Epic.
#    Original Estimate tính bằng giây (Jira yêu cầu); 1 giờ = 3600.
$tasks | ForEach-Object {
    [pscustomobject]@{
        'Issue ID'          = $_.Code
        'Parent'            = if ($_.Level -eq 2) { $_.Code.Split('.')[0] } else { '' }
        'Issue Type'        = if ($_.Level -eq 1) { 'Epic' } else { 'Task' }
        'Summary'           = "$($_.Code) $($_.Name)"
        'Assignee'          = if ($_.Level -eq 2) { $_.Owner } else { '' }
        'Original Estimate' = if ($_.Level -eq 2) { $_.Hours * 3600 } else { '' }
        'Sprint'            = if ($_.Level -eq 2) { "T1 Sprint $($_.From)" } else { '' }
        'Labels'            = "WBS-$($_.Code.Split('.')[0])"
        'Status'            = if ($_.Done) { 'Done' } else { 'To Do' }
        'Due Date'          = if ($_.Level -eq 2) { Format-Date $sprints[$_.To][1] } else { '' }
    }
} | Export-Csv (Join-Path $here 'jira-backlog.csv') -NoTypeInformation -Encoding UTF8

$byOwner = $tasks | Where-Object Level -eq 2 | Group-Object Owner | ForEach-Object { "$($_.Name): $(($_.Group | Measure-Object Hours -Sum).Sum) giờ" }
"Đã xuất wbs-msproject.csv và jira-backlog.csv. Tổng $total giờ. " + ($byOwner -join '; ')
