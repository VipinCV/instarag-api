namespace InstaRAG.Api.Models;

/// <summary>
/// Represents a product record from the CSV/JSON catalog.
/// </summary>
public class ProductData
{
    public string ProductName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public string StockStatus { get; set; } = "In Stock";
    public string Specs { get; set; } = string.Empty;
    public string SizeChart { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ComplementaryProducts { get; set; } = string.Empty;

    /// <summary>
    /// Converts the product data into a structured text document
    /// suitable for RAG ingestion and retrieval.
    /// </summary>
    public string ToDocumentText()
    {
        return $"""
            === PRODUCT INFORMATION ===
            Product Name: {ProductName}
            Category: {Category}
            Price: {Price:N2} {Currency}
            Stock Status: {StockStatus}

            Description:
            {Description}

            Specifications:
            {Specs}

            Size Chart:
            {SizeChart}

            Complementary Products:
            {ComplementaryProducts}
            === END PRODUCT ===
            """;
    }
}
