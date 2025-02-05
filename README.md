⚠ **Note:** This library is intended for educational and demonstration purposes only. While it works as expected, it still requires further refinement for real-world use, especially in terms of usability.

Ideally, the API should be as intuitive as libraries like **Entity Framework Plus**. However, achieving this level of simplicity required careful consideration regarding thread safety. The current implementation is fully thread-safe, even when the same `DeferredContext` is accessed across multiple threads—though this may still depend on the specific use case.

## **Overview**  

This lightweight **.NET 6+** library, compatible with **Entity Framework Core**, enables querying multiple datasets in a **single database round trip**. By reducing unnecessary database calls, it helps mitigate performance issues where multiple queries can be executed together.  

## **Installation & Usage**  

To use this library, simply add the project as a reference in your application.  

### **Example Usage**  

```csharp
var context = new DeferredContext("<your-super-secret-connection-string>");

var older_than_thirty_employees = context.Future(m_DbContext.Employees.Where(i => i.Age >= 30));

var total_invoice_amount = context.FutureSum(
    m_DbContext.Invoices.Where(i => i.CreationTime >= DateTime.Now.AddDays(-1)), 
    i => i.InvoiceAmount
);

var late_employees = context.FutureFirstOrDefault(
    m_DbContext.Employees
        .Include(i => i.AttendanceEntries)
        .Where(i => i.AttendanceEntries.Any(i => i.Type == AttendanceTypeEnum.Late))
);

// Execute all queries in a single round trip
await context.ExecuteAsync();
```

After calling `await context.ExecuteAsync();`, all the variables (`older_than_thirty_employees`, `total_invoice_amount`, `late_employees`) will be populated with their respective results.  
- If the result set contains multiple items, you can access them via `.Items`.  
- If the result is a single value, use `.Value`.  

---

## **Supported Future Methods**  

This library provides the following **deferred execution methods** for batching multiple queries:  

| Method                 | Return Type  |
|------------------------|-------------|
| `.FutureSum()`         | `int` or `int?` |
| `.FutureSum()`         | `long` or `long?` |
| `.FutureCount()`       | `int` |
| `.FutureLongCount()`   | `long` |
| `.FutureSkip()`        | `T[]` |
| `.FutureTake()`        | `T[]` |
| `.FutureFirstOrDefault()` | `T?` |

---

This approach makes querying multiple datasets in a single database round trip **simpler, more efficient, and performance-friendly**. 🚀
