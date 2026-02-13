using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace InventoryManagementAPI.Services
{
    public class ExcelService
    {
        public ExcelService()
        {
            // Set EPPlus license context (NonCommercial for learning/internal use)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public byte[] GenerateCurrentStockReport(List<CurrentStockReportItem> data, string reportTitle)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Current Stock Report");

            // Set title
            worksheet.Cells["A1:H1"].Merge = true;
            worksheet.Cells["A1"].Value = reportTitle;
            worksheet.Cells["A1"].Style.Font.Size = 16;
            worksheet.Cells["A1"].Style.Font.Bold = true;
            worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Set report date
            worksheet.Cells["A2:H2"].Merge = true;
            worksheet.Cells["A2"].Value = $"Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            worksheet.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Headers
            var headerRow = 4;
            worksheet.Cells[headerRow, 1].Value = "Item Code";
            worksheet.Cells[headerRow, 2].Value = "Item Name";
            worksheet.Cells[headerRow, 3].Value = "Category";
            worksheet.Cells[headerRow, 4].Value = "UOM";
            worksheet.Cells[headerRow, 5].Value = "Qty On Hand";
            worksheet.Cells[headerRow, 6].Value = "Min Stock Level";
            worksheet.Cells[headerRow, 7].Value = "Stock Status";
            worksheet.Cells[headerRow, 8].Value = "Last Updated";

            // Format headers
            using (var range = worksheet.Cells[headerRow, 1, headerRow, 8])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
                range.Style.Font.Color.SetColor(Color.White);
                range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                range.Style.Border.BorderAround(ExcelBorderStyle.Thin);
            }

            // Data rows
            var row = headerRow + 1;
            foreach (var item in data)
            {
                worksheet.Cells[row, 1].Value = item.ItemCode;
                worksheet.Cells[row, 2].Value = item.ItemName;
                worksheet.Cells[row, 3].Value = item.CategoryName;
                worksheet.Cells[row, 4].Value = item.UOMCode;
                worksheet.Cells[row, 5].Value = item.QtyOnHand;
                worksheet.Cells[row, 6].Value = item.MinStockLevel;
                worksheet.Cells[row, 7].Value = item.StockStatus;
                worksheet.Cells[row, 8].Value = item.LastUpdated.ToString("yyyy-MM-dd HH:mm");

                // Color code stock status
                var statusCell = worksheet.Cells[row, 7];
                statusCell.Style.Font.Bold = true;
                if (item.StockStatus == "OUT OF STOCK")
                {
                    statusCell.Style.Font.Color.SetColor(Color.Red);
                }
                else if (item.StockStatus == "LOW STOCK")
                {
                    statusCell.Style.Font.Color.SetColor(Color.Orange);
                }
                else
                {
                    statusCell.Style.Font.Color.SetColor(Color.Green);
                }

                row++;
            }

            // Summary section
            var summaryRow = row + 2;
            worksheet.Cells[summaryRow, 1].Value = "SUMMARY";
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            
            worksheet.Cells[summaryRow + 1, 1].Value = "Total Items:";
            worksheet.Cells[summaryRow + 1, 2].Value = data.Count;
            
            worksheet.Cells[summaryRow + 2, 1].Value = "Total Stock Quantity:";
            worksheet.Cells[summaryRow + 2, 2].Value = data.Sum(d => d.QtyOnHand);
            
            worksheet.Cells[summaryRow + 3, 1].Value = "Out of Stock Items:";
            worksheet.Cells[summaryRow + 3, 2].Value = data.Count(d => d.StockStatus == "OUT OF STOCK");
            
            worksheet.Cells[summaryRow + 4, 1].Value = "Low Stock Items:";
            worksheet.Cells[summaryRow + 4, 2].Value = data.Count(d => d.StockStatus == "LOW STOCK");

            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            // Add borders to all data
            using (var range = worksheet.Cells[headerRow, 1, row - 1, 8])
            {
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            return package.GetAsByteArray();
        }

        public byte[] GenerateMonthlyMovementReport(List<MonthlyMovementReportItem> data, string reportPeriod, int year, int month)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Monthly Movement");

            // Title
            worksheet.Cells["A1:I1"].Merge = true;
            worksheet.Cells["A1"].Value = $"Monthly Stock Movement Report - {reportPeriod}";
            worksheet.Cells["A1"].Style.Font.Size = 16;
            worksheet.Cells["A1"].Style.Font.Bold = true;
            worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Report date
            worksheet.Cells["A2:I2"].Merge = true;
            worksheet.Cells["A2"].Value = $"Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            worksheet.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Headers
            var headerRow = 4;
            worksheet.Cells[headerRow, 1].Value = "Item Code";
            worksheet.Cells[headerRow, 2].Value = "Item Name";
            worksheet.Cells[headerRow, 3].Value = "Category";
            worksheet.Cells[headerRow, 4].Value = "UOM";
            worksheet.Cells[headerRow, 5].Value = "Opening Stock";
            worksheet.Cells[headerRow, 6].Value = "Inward";
            worksheet.Cells[headerRow, 7].Value = "Outward";
            worksheet.Cells[headerRow, 8].Value = "Adjustments";
            worksheet.Cells[headerRow, 9].Value = "Closing Stock";

            // Format headers
            using (var range = worksheet.Cells[headerRow, 1, headerRow, 9])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(79, 129, 189));
                range.Style.Font.Color.SetColor(Color.White);
                range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            // Data rows
            var row = headerRow + 1;
            foreach (var item in data)
            {
                worksheet.Cells[row, 1].Value = item.ItemCode;
                worksheet.Cells[row, 2].Value = item.ItemName;
                worksheet.Cells[row, 3].Value = item.CategoryName;
                worksheet.Cells[row, 4].Value = item.UOMCode;
                worksheet.Cells[row, 5].Value = item.OpeningStock;
                worksheet.Cells[row, 6].Value = item.Inward;
                worksheet.Cells[row, 7].Value = item.Outward;
                worksheet.Cells[row, 8].Value = item.Adjustments;
                worksheet.Cells[row, 9].Value = item.ClosingStock;

                // Format numbers
                worksheet.Cells[row, 5, row, 9].Style.Numberformat.Format = "#,##0.00";

                // Highlight negative adjustments
                if (item.Adjustments < 0)
                {
                    worksheet.Cells[row, 8].Style.Font.Color.SetColor(Color.Red);
                }

                row++;
            }

            // Totals row
            var totalRow = row;
            worksheet.Cells[totalRow, 1].Value = "TOTALS";
            worksheet.Cells[totalRow, 1].Style.Font.Bold = true;
            worksheet.Cells[totalRow, 5].Value = data.Sum(d => d.OpeningStock);
            worksheet.Cells[totalRow, 6].Value = data.Sum(d => d.Inward);
            worksheet.Cells[totalRow, 7].Value = data.Sum(d => d.Outward);
            worksheet.Cells[totalRow, 8].Value = data.Sum(d => d.Adjustments);
            worksheet.Cells[totalRow, 9].Value = data.Sum(d => d.ClosingStock);

            // Format totals
            using (var range = worksheet.Cells[totalRow, 1, totalRow, 9])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Double;
            }

            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            // Add borders
            using (var range = worksheet.Cells[headerRow, 1, totalRow, 9])
            {
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            return package.GetAsByteArray();
        }
    }

    // DTOs for Excel Service
    public class CurrentStockReportItem
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string UOMCode { get; set; } = string.Empty;
        public decimal QtyOnHand { get; set; }
        public decimal MinStockLevel { get; set; }
        public string StockStatus { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
    }

    public class MonthlyMovementReportItem
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string UOMCode { get; set; } = string.Empty;
        public decimal OpeningStock { get; set; }
        public decimal Inward { get; set; }
        public decimal Outward { get; set; }
        public decimal Adjustments { get; set; }
        public decimal ClosingStock { get; set; }
    }
}