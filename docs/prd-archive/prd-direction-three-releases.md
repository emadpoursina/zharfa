> **ARCHIVED — not scope authority.** Source of the three-release product direction (approved project direction, received 2026-11-16), reconciled into [`zharfa-prd.md`](../../zharfa-prd.md) v3.0. Do not plan or build from this file.

# Product Requirements Document (PRD)
# Pharmaceutical Distribution Business Intelligence & AI Platform

**Project:** Pharmacy Analytics  
**Document Version:** 1.0  
**Status:** Approved Project Direction / Requirements to be Refined  
**Product Type:** Desktop Business Intelligence & AI Reporting Application  
**Target Industry:** Pharmaceutical Distribution  
**Primary Language:** Persian (RTL)  
**Deployment:** Self-hosted / On-premises  
**Existing Operational System:** سیستم جامع شرکت مبنا

---

# 1. Executive Summary

## 1.1 Project Overview

The Pharmaceutical Distribution Business Intelligence & AI Platform is an integrated reporting, analytics, and artificial intelligence application designed for a pharmaceutical distribution company.

The company currently uses an existing operational software system called **سیستم جامع شرکت مبنا** for its daily business operations and data entry.

The purpose of this project is not to replace or modify the existing system.

Instead, the new application will connect to the existing مبنا database through controlled, read-only access, retrieve relevant business information, analyze it, and present it through modern dashboards, reports, and an AI-powered conversational interface.

The platform will enable management and authorized employees to:

- Access important business information through centralized dashboards.
- Analyze purchasing activities and vendor relationships.
- Monitor sales performance and pharmacy customers.
- Understand warehouse operations and inventory conditions.
- Generate reports and compare business performance across different domains.
- Ask questions about company data using natural language.
- Receive AI-generated insights and explanations based on actual business data.

The platform will be developed in three independently usable releases:

1. Vendor & Purchasing Analytics
2. Pharmacy Sales Analytics
3. Warehouse & Inventory Analytics

All three releases will belong to one integrated application with a shared data architecture and a centralized AI agent.

## 1.2 Product Vision

To transform the company's existing operational data into accessible, reliable, and actionable business intelligence, helping management make better decisions without changing its existing operational workflows.

---

# 2. Business Context

## 2.1 Current Situation

The company currently manages its daily business operations through سیستم جامع شرکت مبنا.

The existing system is responsible for operational activities and data entry.

However, accessing, combining, understanding, and analyzing business information for management decisions can be difficult.

Management needs a better way to:

- Understand the overall condition of the business.
- Identify purchasing and sales trends.
- Monitor suppliers and pharmacy customers.
- Analyze inventory and warehouse performance.
- Identify potential business risks.
- Compare information from different departments.
- Obtain answers to business questions without manually searching through multiple reports.

## 2.2 Proposed Solution

Build a separate Business Intelligence and AI platform that uses the existing مبنا database as its primary data source.

The new platform will provide:

1. Centralized business dashboards.
2. Dedicated analytics modules.
3. Cross-domain reporting.
4. An AI-powered data assistant.
5. Custom analytical reports.
6. Data visualization and business insights.

The existing operational system will remain the single source of truth for operational records.

---

# 3. Product Goals and Objectives

## 3.1 Primary Goals

- Centralize access to important business information.
- Reduce dependency on manually prepared reports.
- Improve management visibility into business performance.
- Enable cross-departmental analysis.
- Make business data accessible through natural-language questions.
- Identify trends, anomalies, and potential risks.
- Provide reliable and consistent business metrics.
- Build a scalable architecture that supports future analytical capabilities.

## 3.2 Success Criteria

The platform should be considered successful when:

- Authorized users can access the relevant business dashboards.
- Reports display accurate information based on the مبنا database.
- Users can filter and analyze data without technical knowledge.
- The AI agent can answer questions using actual company data.
- The AI agent can combine information from multiple business domains.
- Management can compare purchasing, sales, and inventory information.
- Each development phase can be deployed and used independently.
- The application does not modify or interfere with the operational database.

---

# 4. Product Architecture

## 4.1 Overall Architecture

The product consists of one integrated application with three business modules and one centralized AI intelligence layer.

### Main Components

1. Presentation Layer
2. Business Analytics Layer
3. Central AI Agent
4. Data Integration Layer
5. Reporting Database / Data Layer
6. Existing مبنا Database

### Logical Architecture

```text
                 USERS
                   |
                   v
       PHARMACEUTICAL ANALYTICS
                   |
       +-----------+-----------+
       |           |           |
       v           v           v
    VENDORS       SALES     WAREHOUSE
       |           |           |
       +-----------+-----------+
                   |
                   v
          CENTRAL AI AGENT
                   |
                   v
       BUSINESS ANALYTICS LAYER
                   |
                   v
          DATA INTEGRATION
                   |
                   v
             مبنا DATABASE
              READ ONLY
```

The AI agent must be able to access all available business domains through a shared and controlled data access layer.

## 4.2 Application Structure

The application will have the following primary navigation sections:

| Section | Description |
|---|---|
| Overview | Company-wide management dashboard |
| Vendors | Vendor and purchasing analytics |
| Sales | Pharmacy sales analytics |
| Warehouse | Inventory and warehouse analytics |
| AI Assistant | Conversational access to company data |
| Reports | Saved and custom reports |
| Settings | Application configuration and administration |

Some sections will become fully functional during later development phases.

However, the underlying architecture must support the complete application from the beginning.

## 4.3 Centralized Data Architecture

The application must use a shared data architecture.

Each business module must use common business entities and relationships where applicable.

Important shared entities include:

- Products
- Vendors
- Pharmacies
- Sales transactions
- Purchase transactions
- Inventory records
- Warehouse movements
- Financial records
- Employees and sales representatives
- Dates and reporting periods

The system must maintain consistent definitions for shared business metrics.

This is particularly important for reports combining information from multiple modules.

---

# 5. Development Strategy

The project will be delivered in three major development phases.

Each phase must produce an independently usable software release.

The application architecture must support all three phases from the beginning.

## Phase 1: Vendor & Purchasing Analytics

### 5.1 Objective

Provide comprehensive visibility into the company's purchasing activities, supplier relationships, and procurement performance.

### 5.2 Functional Scope

Potential reports and analytics include:

**Vendor Analysis**
- Vendor directory and profiles.
- Total purchases by vendor.
- Purchase history by vendor.
- Vendor purchasing trends.
- Vendor performance indicators.
- Vendor comparison reports.

**Purchasing Analysis**
- Total purchase amounts.
- Purchase quantities by product.
- Purchase trends over time.
- Purchasing breakdown by product.
- Purchasing breakdown by vendor.
- Most purchased products.
- Purchase returns.
- Purchase transaction history.

**Financial Analysis**
- Vendor balances.
- Outstanding vendor payments.
- Payment history.
- Purchasing amounts versus payments.
- Vendor financial summaries.

Financial reports will depend on the availability and structure of relevant financial data in the مبنا database.

### 5.3 Phase 1 Deliverables

- Database integration foundation.
- Vendor and purchasing data models.
- Vendor analytics dashboard.
- Purchasing reports.
- Filtering and date-range selection.
- Vendor comparison reports.
- Initial centralized AI agent integration.
- First independently usable application release.

### 5.4 Expected Business Value

Management will be able to understand:

- Which vendors represent the largest purchasing volumes?
- Which products are purchased most frequently?
- How purchasing behavior changes over time.
- How much money is spent with different vendors.
- Which vendor relationships require further investigation?

---

## Phase 2: Pharmacy Sales Analytics

### 6.1 Objective

Provide comprehensive visibility into sales activities, pharmacy customers, revenue performance, and customer purchasing behavior.

### 6.2 Functional Scope

**Sales Analysis**
- Total sales.
- Sales trends over time.
- Sales by product.
- Sales by pharmacy.
- Sales by sales representative.
- Sales by product category.
- Most and least sold products.
- Sales returns.
- Sales transaction history.

**Pharmacy Analysis**
- Pharmacy directory and profiles.
- Pharmacy purchasing history.
- Total purchases by pharmacy.
- Pharmacy purchasing trends.
- Customer segmentation.
- Top customers.
- Inactive or declining customers.

**Financial Analysis**
- Sales amounts.
- Outstanding pharmacy balances.
- Customer payment history.
- Sales versus collections.
- Customer financial summaries.

The availability of individual financial indicators must be verified against the source database.

### 6.3 Cross-Domain Reporting

Phase 2 must expand the reporting architecture to include comparisons between vendors and sales.

Examples:

- Purchasing volume versus sales volume.
- Purchase prices versus selling prices.
- Purchased products versus sold products.
- Purchasing trends versus sales trends.
- Product-level purchasing and sales performance.

These reports must use consistent product identification and reporting periods.

Profitability calculations must account for appropriate inventory costing, returns, discounts, and other relevant costs.

A simple difference between purchase and selling prices must not automatically be presented as actual profit.

### 6.4 Phase 2 Deliverables

- Pharmacy sales analytics dashboard.
- Sales reports.
- Customer analytics.
- Sales representative analytics.
- Financial sales reports where supported.
- Vendor and sales cross-domain reports.
- Expanded AI capabilities.
- Second independently usable application release.

### 6.5 Expected Business Value

Management will be able to understand:

- Which pharmacies generate the most revenue?
- Which products perform best?
- Which customers have declining purchases?
- Which products have high purchasing volumes but low sales?
- How sales performance changes over time?
- How purchasing decisions relate to sales performance?

---

## Phase 3: Warehouse & Inventory Analytics

### 7.1 Objective

Provide comprehensive visibility into warehouse operations, inventory levels, stock movements, and inventory-related risks.

### 7.2 Functional Scope

**Inventory Analysis**
- Current inventory levels.
- Stock quantities by product.
- Stock distribution by warehouse.
- Inventory valuation.
- Product stock history.
- Inventory changes over time.

**Warehouse Operations**
- Incoming goods.
- Outgoing goods.
- Stock movements.
- Warehouse transfers.
- Inventory adjustments.
- Warehouse movement history.

**Inventory Risk Analysis**
- Expired products.
- Products approaching expiration.
- Low-stock products.
- Overstocked products.
- Slow-moving products.
- Dead stock.
- Inventory turnover.

**Product Performance**
- Stock levels versus sales.
- Stock levels versus purchasing.
- Product demand trends.
- Inventory turnover analysis.
- Product lifecycle analysis.

All reports must be implemented according to the actual data available in مبنا.

### 7.3 Cross-Domain Reporting

Phase 3 will complete the cross-domain reporting capabilities.

Examples:

- Purchasing versus sales versus inventory.
- Inventory levels versus sales demand.
- Purchasing volume versus current stock.
- Product profitability and inventory investment.
- Stock movement versus product sales.
- Inventory turnover versus purchasing frequency.

### 7.4 Phase 3 Deliverables

- Warehouse analytics dashboard.
- Inventory reports.
- Stock movement reports.
- Expiration monitoring.
- Inventory risk reports.
- Full cross-domain reporting.
- Complete centralized AI analytics capabilities.
- Third independently usable application release.

### 7.5 Expected Business Value

Management will be able to understand:

- What products are currently available?
- Which products are approaching expiration?
- Which products are accumulating in inventory?
- Which products are selling too slowly?
- Which products may require replenishment?
- How efficiently is the company's inventory being managed?

---

# 8. Central AI Agent

## 8.1 Overview

The platform must include one centralized AI agent capable of accessing and analyzing information from all three business domains.

The AI agent is a shared platform capability, not a separate feature belonging to only one development phase.

Its capabilities will expand as new business modules become available.

## 8.2 Core Responsibilities

The AI agent must:

- Answer natural-language questions about company data.
- Retrieve relevant information from the analytics layer.
- Analyze business performance.
- Compare different business entities.
- Combine information from multiple domains.
- Explain trends and changes.
- Identify unusual patterns.
- Generate tables and charts.
- Produce analytical summaries.
- Help users investigate business questions.
- Generate dynamic reports based on user requests.

## 8.3 Natural-Language Queries

Users should not need to understand database structures, table names, or SQL.

Examples:

**Vendor Questions**
- Which vendor had the highest purchasing volume last month?
- Compare the purchasing trends of our top five vendors.
- Which products have experienced the largest increase in purchasing prices?

**Sales Questions**
- Which pharmacies generated the highest sales this year?
- What are our best-selling products?
- Which customers have reduced their purchases?

**Warehouse Questions**
- Which products are approaching expiration?
- Which products have remained in stock for too long?
- What is our current inventory valuation?

**Cross-Domain Questions**
- Which products have high purchasing volume but low sales?
- Compare purchasing and sales trends for the same products.
- Which products have high inventory levels and declining sales?
- Which products represent the largest inventory investment?
- How does our purchasing behavior relate to sales performance?

## 8.4 Dynamic Report Generation

The AI agent should be able to generate reports that do not necessarily exist as predefined dashboard pages.

For example:

User:

"Compare the top 10 purchased products with their sales volume during the last six months."

Expected response:

- Identify the relevant products.
- Retrieve purchasing information.
- Retrieve sales information.
- Match the products across both datasets.
- Calculate the comparison.
- Present the results in a table or chart.
- Explain the most important findings.

## 8.5 AI Data Access Architecture

The AI agent should not receive unrestricted access to the production database.

Instead, it should access data through a controlled analytics layer.

Recommended architecture:

```text
             USER QUESTION
                   |
                   v
             CENTRAL AI
                   |
                   v
       QUESTION INTERPRETATION
                   |
                   v
        BUSINESS METRIC LAYER
                   |
                   v
       CONTROLLED QUERY ENGINE
                   |
                   v
         READ-ONLY DATA ACCESS
                   |
                   v
           ANALYTICS DATA
                   |
                   v
       VALIDATION AND PROCESSING
                   |
                   v
          AI-GENERATED RESPONSE
```

The system must validate queries, enforce permissions, and prevent unauthorized data access.

## 8.6 AI Accuracy and Transparency

The AI agent must:

- Base factual answers on retrieved company data.
- Avoid inventing missing information.
- Clearly identify unavailable data.
- Distinguish between actual figures and estimates.
- Explain the reporting period used.
- Identify the relevant data sources.
- Display calculation assumptions where appropriate.
- Avoid presenting correlation as causation.
- Allow users to inspect supporting records where authorized.

---

# 9. Reporting and Dashboard Requirements

## 9.1 Overview Dashboard

The application must provide a company-wide overview dashboard.

This dashboard should give management a quick understanding of the company's overall performance.

Potential indicators:

- Total purchases.
- Total sales.
- Sales trends.
- Purchasing trends.
- Total inventory value.
- Number of active vendors.
- Number of active pharmacies.
- Inventory risks.
- Expiring products.
- Outstanding balances.

The exact KPIs will be finalized after database discovery and business requirements validation.

## 9.2 Dashboard Capabilities

Dashboards should support:

- Interactive charts.
- Summary cards.
- Data tables.
- Date-range filters.
- Product filters.
- Vendor filters.
- Pharmacy filters.
- Warehouse filters.
- Drill-down into detailed information.
- Comparison between reporting periods.
- Exporting reports where supported.

## 9.3 Standard Report Requirements

Reports should support:

- Search.
- Filtering.
- Sorting.
- Pagination.
- Date selection.
- Data export.
- Detailed transaction views.
- Consistent metric definitions.

## 9.4 Custom Reports

Users should be able to request additional analytical reports through the AI agent.

The system should also support saving frequently used reports for later access, subject to permissions.

---

# 10. Data Integration Requirements

## 10.1 Existing Data Source

The primary data source is the existing سیستم جامع شرکت مبنا database.

The new platform must not replace the operational database.

## 10.2 Database Access

The application must use controlled, read-only access to the existing database.

Requirements:

- Dedicated read-only database credentials.
- No INSERT operations.
- No UPDATE operations.
- No DELETE operations.
- No modification of operational database structures.
- Restricted access to approved tables and views.
- Appropriate query limits and resource controls.

## 10.3 Data Synchronization

The system must retrieve and update analytical data from the existing database.

The synchronization strategy must be determined after examining:

- Database size.
- Transaction volume.
- Available timestamps.
- Existing primary and foreign keys.
- Database indexing.
- Data update patterns.
- Operational workload.

Possible approaches include:

- Scheduled incremental synchronization.
- Full synchronization for initial loading.
- Read-only direct querying for selected use cases.
- Database views.
- A separate reporting database.

The final architecture should minimize the performance impact on the operational system.

## 10.4 Data Validation

The platform must validate incoming data for:

- Missing values.
- Duplicate records.
- Invalid relationships.
- Inconsistent product identifiers.
- Incorrect dates.
- Unexpected quantities.
- Inconsistent financial values.

Data quality issues should be visible to administrators and must not silently produce misleading reports.

---

# 11. Security and Permissions

## 11.1 Authentication

The platform must provide secure user authentication.

## 11.2 Role-Based Access Control

The system should support configurable user roles.

Potential roles:

- Administrator.
- Management.
- Finance.
- Purchasing.
- Sales.
- Warehouse.
- General Viewer.

The final permission structure will be determined according to company requirements.

## 11.3 Data Permissions

Permissions must apply to:

- Dashboard sections.
- Reports.
- Detailed transactions.
- Financial information.
- AI queries.
- Data exports.

The AI agent must enforce the same access restrictions as the regular application interface.

Users must not be able to retrieve restricted information through natural-language queries.

## 11.4 Audit Logging

The platform should record important activities, including:

- User authentication.
- Report access.
- Data exports.
- AI queries.
- Administrative changes.
- Permission changes.

Sensitive data must be protected in application logs.

---

# 12. Non-Functional Requirements

## 12.1 Deployment

- Self-hosted deployment.
- Compatible with the company's internal infrastructure.
- No mandatory cloud dependency.
- Support for locally hosted AI models.
- Optional external AI API integration.

## 12.2 Language

- Primary interface language: Persian.
- Full right-to-left interface support.
- Persian date/calendar support where required.
- Support for Persian and English text in business data.
- Consistent number and currency formatting.

## 12.3 Performance

The application should provide responsive dashboard interactions and efficient report generation.

Performance requirements will be defined after measuring:

- Database size.
- Number of records.
- Number of users.
- Reporting complexity.
- Synchronization frequency.

Heavy analytical workloads should not negatively affect the operational مبنا system.

## 12.4 Scalability

The architecture must support:

- Increasing transaction volumes.
- Additional business reports.
- Additional analytical dimensions.
- More users.
- Expanded AI capabilities.
- Future integrations.

## 12.5 Reliability

The platform should support:

- Error handling.
- Data synchronization monitoring.
- Application logging.
- Backup and recovery procedures.
- Graceful handling of unavailable AI services.
- Graceful handling of database connection failures.

---

# 13. AI Model and Provider Configuration

The platform should support two AI deployment approaches.

## 13.1 Local AI Models

Users should be able to configure a locally hosted AI model.

Benefits:

- Greater control over data processing.
- Reduced external dependency.
- Potentially lower recurring API costs.
- Suitable for on-premises environments.

## 13.2 External AI APIs

The application should optionally support external AI providers through API keys.

Requirements:

- Configurable API keys.
- Provider configuration.
- Model selection.
- Connection testing.
- Error handling.
- Configurable usage limits where supported.

The AI integration architecture should be provider-independent wherever practical.

---

# 14. User Experience Requirements

The platform should be designed for business users and management rather than technical database users.

Design principles:

- Clean and professional interface.
- Simple navigation.
- Persian-first experience.
- Consistent dashboard layouts.
- Clear charts and visual indicators.
- Easy-to-understand financial information.
- Minimal technical terminology.
- Accessible report filtering.
- Conversational AI interface.

The application should make important business information easy to discover without requiring users to understand how the underlying database works.

---

# 15. Development and Release Strategy

The platform will be developed incrementally.

Each phase must produce a working and independently usable release.

| Release | Main Scope | Deliverable |
|---|---|---|
| Release 1 | Foundation + Vendors | Vendor and purchasing analytics |
| Release 2 | Sales | Sales analytics and vendor-sales comparisons |
| Release 3 | Warehouse | Inventory analytics and full cross-domain reporting |

## 15.1 Shared Foundation

The following components should be designed and implemented as shared platform infrastructure:

- Application architecture.
- Authentication.
- User permissions.
- Database integration.
- Data synchronization.
- Shared data models.
- Business metric definitions.
- AI integration framework.
- Reporting engine.
- Logging and monitoring.

These components should not be independently rebuilt for each phase.

## 15.2 Backward Compatibility

New releases must preserve the functionality of previously delivered modules.

The addition of a new module must not break existing reports or AI capabilities.

---

# 16. Database Discovery and Analysis

Database discovery is a mandatory prerequisite for finalizing detailed report specifications.

Before implementing the first analytics module, the development team must examine the existing مبنا database.

## 16.1 Discovery Objectives

Identify:

- Database engine and version.
- Database structure.
- Relevant tables and views.
- Primary keys.
- Foreign keys.
- Relationships between business entities.
- Transaction structures.
- Financial data structures.
- Inventory data structures.
- Date and time fields.
- Product identifiers.
- Vendor identifiers.
- Pharmacy identifiers.

## 16.2 Business Domain Mapping

The database must be mapped into the following business domains:

| Domain | Information to Identify |
|---|---|
| Vendors | Suppliers, purchases, purchase returns, vendor balances |
| Sales | Pharmacies, sales, sales returns, payments |
| Warehouse | Inventory, warehouses, stock movements, expiration dates |
| Shared | Products, employees, dates, financial entities |

## 16.3 Discovery Deliverables

- Database structure documentation.
- Entity Relationship Diagram (ERD).
- Business domain mapping.
- Data dictionary.
- Data quality assessment.
- Initial report feasibility assessment.
- Recommended synchronization strategy.
- Initial KPI and metrics catalogue.

## 16.4 Report Validation

Every proposed report must be evaluated against the actual database.

The development team must identify:

1. Required source tables.
2. Required fields.
3. Relevant relationships.
4. Calculation methodology.
5. Missing or unavailable data.
6. Potential data quality issues.

Reports must not be implemented using assumed data structures.

---

# 17. Out of Scope

The following functionalities are not part of the initial project scope unless separately approved:

- Replacing سیستم جامع شرکت مبنا.
- Performing daily operational data entry.
- Modifying purchasing transactions.
- Creating sales transactions.
- Managing warehouse operations directly.
- Modifying product or vendor master records.
- Executing financial transactions.
- Automating operational workflows.
- Replacing the company's accounting system.
- Directly controlling pharmaceutical distribution processes.

The platform is primarily an analytics, reporting, and decision-support application.

---

# 18. Risks and Considerations

## 18.1 Database Complexity

The existing database may contain complex or undocumented relationships.

Mitigation: Conduct database discovery before finalizing report specifications.

## 18.2 Data Quality

Incorrect or inconsistent operational records can produce inaccurate analytical results.

Mitigation: Implement validation, data quality monitoring, and transparent reporting assumptions.

## 18.3 AI Hallucinations

AI models may produce incorrect interpretations or calculations.

Mitigation: Use validated business metrics, controlled data access, query validation, and evidence-based responses.

## 18.4 Database Performance

Analytical queries may affect the performance of the operational system.

Mitigation: Prefer incremental synchronization and a separate analytics/reporting layer where appropriate.

## 18.5 Cross-Domain Data Consistency

Reports combining multiple domains may produce incorrect results if entity relationships or calculation rules are inconsistent.

Mitigation: Establish shared data models and centralized business metric definitions.

## 18.6 Scope Expansion

The company may discover additional reporting requirements during development.

Mitigation: Maintain a prioritized report catalogue and separate initial release requirements from future enhancements.

---

# 19. Final Product Definition

The final product is one integrated Pharmaceutical Business Intelligence and AI platform consisting of:

**Three Business Modules**

1. Vendor & Purchasing Analytics.
2. Pharmacy Sales Analytics.
3. Warehouse & Inventory Analytics.

**One Centralized AI Agent**

A conversational intelligence layer with access to all implemented business domains, capable of answering questions, generating dynamic reports, and performing cross-domain analysis.

**One Shared Data Architecture**

A controlled, read-only integration with the existing مبنا database, supported by a centralized analytics and reporting layer.

**Three Independently Usable Releases**

Each development phase will deliver practical business value without requiring the completion of the entire platform.

The existing سیستم جامع شرکت مبنا remains the source of truth for operational business data.

The new platform transforms that data into accessible information, meaningful analysis, and actionable business intelligence.

---

# 20. Confirmed Project Decisions

The following decisions are confirmed and should be treated as architectural constraints:

1. One application with three business modules.
2. One centralized AI agent with access to all three business domains.
3. Cross-domain reports are a core feature, not an afterthought.
4. Controlled, read-only access to the existing مبنا database.
5. Three independently usable development releases.
6. The existing مبنا system remains the source of truth.
7. The new application focuses on analytics, reporting, and AI-assisted decision support.
8. The AI agent must support both predefined and dynamically generated reports.
9. The platform should support local AI models and external API providers.
10. The platform should be self-hosted and primarily Persian-language.

**Document Status:** Approved Project Direction. Detailed technical specifications, report definitions, and KPI calculations remain subject to database discovery and business validation.
