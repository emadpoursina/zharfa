# Product Requirements Document (PRD)
# Pharmaceutical Distribution Business Intelligence & AI Platform

**Project:** Pharmecy Analytics  
**Document version:** 1.1  
**Status:** Draft for MVP specialist review  
**Scope:** Initial demo, MVP, and future integration architecture

---

# MVP Definition

| Attribute | Value |
|---|---|
| Project | Pharmecy Analytics |
| Product type | Business Intelligence & AI Analytics |
| Industry | Pharmaceutical Distribution |
| Primary users | Manager, Operators, Administrator |
| Deployment | Self-hosted |
| Primary interface language | Persian (RTL) |
| AI support | Local models and cloud APIs |
| Existing operational system | سیستم جامع شرکت مبنا |
| Expected legacy database | Microsoft SQL Server (unverified) |
| MVP purpose | Management analytics and decision support |
| Initial data source | Synthetic pharmaceutical distribution data |
| Windows support | Required for MVP |
| Future mobile support | Required architectural capability; not part of MVP |

---

# 1. Executive Summary

## 1.1 Project Overview

The objective of this project is to develop a self-hosted Business Intelligence (BI) and AI-powered analytics platform for a pharmaceutical distribution company.

The company currently uses an existing management and accounting application named **سیستم جامع شرکت مبنا** for its daily operations and data entry.

Although the existing application appears to cover many operational requirements, the company faces two major challenges:

1. Management cannot easily generate meaningful reports or obtain a clear overview of business performance.
2. Management does not yet have a clearly defined understanding of which reports, metrics, and analytical insights would be most useful for decision-making.

Instead of replacing the existing application, this project will initially introduce an independent analytics layer that extracts relevant information from available business data, processes it, and presents it through interactive dashboards, reports, and AI-powered analytical tools.

The platform will initially be developed using a synthetic database designed around pharmaceutical distribution workflows.

After the initial demonstration and company approval, the platform will be integrated with the actual legacy database.

The long-term direction of the Mabna system remains undecided. The architecture must therefore avoid unnecessary dependency on Mabna while also avoiding the premature development of a replacement ERP.

## 1.2 Core Concept

**Keep the existing operational system. Build a new intelligence layer around it.**

The platform will not replace the existing accounting software or interfere with its operational workflows.

Its initial responsibility is to transform existing business data into useful information for management and decision-making.

## 1.3 Project Goals

- Improve management visibility into business performance.
- Provide centralized dashboards and reporting.
- Make business information easier to understand and explore.
- Help management discover its actual reporting requirements.
- Introduce AI-powered business analysis.
- Provide an AI assistant to help users operate the platform.
- Create an extensible architecture for future analytical capabilities.
- Minimize disruption to existing company operations.
- Establish an architecture that can later support mobile devices without rebuilding the application.

---

# 2. Problem Statement

## 2.1 Problem 1: Lack of Reporting and Business Visibility

The company already records operational information in its existing software, but extracting meaningful reports is difficult.

As a result, management cannot easily answer questions such as:

- How much did the company sell this month?
- How does this month's performance compare with previous months?
- Which products generate the most revenue?
- Which customers contribute most to sales?
- How much inventory is currently available?
- Which products are approaching expiration?
- How much money do customers owe the company?
- How much does the company owe its suppliers?
- Which business areas require attention?

The required information may already exist in the database, but it is not presented in a convenient and understandable format.

## 2.2 Problem 2: Undefined Management Requirements

The manager does not currently have a clear list of required reports, KPIs, or analytical questions.

Simply asking management to specify all required reports is unlikely to produce a complete set of requirements.

The project must support a discovery process in which management can:

1. Explore an initial collection of standard business reports.
2. Understand available business metrics.
3. Identify useful information.
4. Ask additional questions.
5. Refine dashboards based on actual business needs.

The initial dashboard will therefore serve two purposes:

- Providing immediate business value.
- Helping discover and validate management requirements.

---

# 3. Target Users and Roles

The initial platform will support three primary user roles.

## 3.1 Manager

The manager is the primary consumer of business intelligence.

Responsibilities and capabilities:

- View management dashboards.
- Access business reports.
- Monitor business performance.
- Compare different periods.
- Explore financial and operational metrics.
- Interact with the AI Business Analyst.
- Receive analytical insights and recommendations.

## 3.2 Operator

Operators primarily use the existing operational software.

Within the new platform, their initial responsibilities include:

- Accessing permitted reports.
- Understanding available dashboard features.
- Using the Platform Assistant.
- Getting help with navigation and terminology.

Operators should only have access to information permitted by their assigned role.

## 3.3 Administrator

The administrator manages the platform.

Responsibilities:

- Manage users and roles.
- Configure system settings.
- Configure AI providers and models.
- Manage database connections.
- Monitor synchronization processes.
- Access system logs.
- Manage platform documentation and configuration.

---

# 4. Product Scope

The proposed MVP consists of four main modules.

## 4.1 Management Dashboard

**Objective:** Provide management with a centralized overview of company performance.

### Proposed dashboard components

**Sales Overview**
- Total sales.
- Sales trends.
- Sales comparisons between periods.
- Number of sales transactions.
- Average transaction value.

**Purchases Overview**
- Total purchases.
- Purchase trends.
- Purchase comparisons.
- Supplier purchasing distribution.

**Inventory Overview**
- Total inventory quantity.
- Inventory valuation.
- Low-stock products.
- Out-of-stock products.
- Expiring and expired products, where batch data is available.

**Financial Overview**
- Outstanding customer receivables.
- Outstanding supplier payables.
- Payments received.
- Payments made.
- Relevant financial summaries.

**Business Performance**
- Top-selling products.
- Top customers.
- Sales by product category.
- Sales trends over time.

### Dashboard capabilities

- Select date ranges.
- Apply filters.
- Navigate between report categories.
- Drill down into detailed information where supported.
- View supporting data behind displayed metrics.
- Compare current and previous periods.
- Adapt dashboard layouts to desktop and smaller screens.

The initial dashboard should prioritize the most useful management indicators rather than attempting to display every available metric.

## 4.2 Reporting Module

**Objective:** Provide detailed reports for exploring business information.

| Category | Proposed reports |
|---|---|
| Sales | Sales by date, product, customer, category |
| Purchases | Purchases by date, supplier, product |
| Inventory | Stock levels, stock movement, inventory valuation |
| Products | Product performance and sales ranking |
| Customers | Customer activity and account balances |
| Suppliers | Supplier activity and account balances |
| Finance | Receivables, payables, payments and financial summaries |

### Report functionality

- Date filtering.
- Category filtering.
- Search and sorting.
- Detailed record exploration.
- Export to Excel.
- Export to PDF.

The availability of individual reports will depend on the data available in the legacy system and the completeness of the synthetic data model.

## 4.3 AI Business Analyst

**Objective:** Provide management with an AI-powered interface for understanding business performance.

The AI Business Analyst should allow users to ask natural-language questions in Persian.

### Example questions

- میزان فروش این ماه چقدر بوده؟
- فروش این ماه نسبت به ماه قبل چقدر تغییر کرده؟
- پرفروش‌ترین محصولات شرکت کدام هستند؟
- کدام مشتریان بیشترین سهم از فروش را دارند؟
- چه محصولاتی موجودی کمی دارند؟
- وضعیت بدهی مشتریان چگونه است؟
- چه تغییرات غیرعادی در عملکرد شرکت مشاهده می‌شود؟

### Core capabilities

1. Answer questions using actual business data.
2. Retrieve approved business metrics.
3. Compare different time periods.
4. Identify significant changes and unusual patterns.
5. Explain dashboard metrics.
6. Identify areas that deserve management attention.
7. Generate data-supported suggestions.
8. Provide supporting figures and relevant data for conclusions.

### Analytical Insights

The agent should eventually be able to identify situations such as:

- Significant decreases in sales.
- Unusual increases in purchases.
- Products with declining sales.
- Customers with increasing outstanding balances.
- Products approaching expiration.
- Significant changes in inventory levels.

These insights must be based on available data and clearly distinguish facts from AI-generated interpretations.

The agent must not invent missing information or present unsupported recommendations as established facts.

## 4.4 Platform Assistant

**Objective:** Help operators and other users understand and use the new platform.

### Core capabilities

- Explain platform functionality.
- Answer questions about available features.
- Explain how to access reports.
- Guide users through dashboard navigation.
- Explain business and financial terminology.
- Provide assistance using platform documentation.

### Example questions

- چطور گزارش فروش ماهانه را مشاهده کنم؟
- چطور فروش دو ماه را مقایسه کنم؟
- ترازنامه را از کجا پیدا کنم؟
- چطور گزارش را به اکسل خروجی بگیرم؟

The assistant should have access to relevant platform documentation and approved platform tools.

It should not have unrestricted access to financial or operational information.

---

# 5. AI and Agent Architecture

## 5.1 General Requirements

The platform must support both local AI models and cloud-based AI providers.

Administrators should be able to configure the AI provider and model through platform settings.

Supported integration approaches should include:

- Local model endpoints, such as Ollama.
- API-key-based model providers.
- Configurable model selection.
- Different model configurations for different agents.
- A provider abstraction that avoids unnecessary dependency on one AI vendor.

The AI provider integration must be handled by the backend, not directly by desktop or mobile clients.

## 5.2 AI Agents

The platform will initially contain two logically separate agents.

| Agent | Primary responsibility | Permitted access |
|---|---|---|
| Platform Assistant | Platform help and guidance | Documentation and platform tools |
| Business Analyst | Business analysis and reporting | Approved analytical tools and metrics |

The agents may share the same AI provider infrastructure but must have separate instructions, tools, and permission boundaries.

## 5.3 Business Analytics Engine

The Business Analyst should use a controlled analytics interface rather than unrestricted access to the operational database.

The preferred approach is to provide approved analytical tools and a semantic layer containing standardized business metrics.

Potential capabilities:

- Retrieve sales summaries.
- Retrieve purchase summaries.
- Retrieve inventory status.
- Retrieve customer and supplier balances.
- Compare business periods.
- Retrieve product rankings.
- Retrieve sales trends.
- Query approved reporting datasets.

Natural-language-to-SQL functionality may be evaluated as an additional capability.

If implemented, generated SQL must be restricted to approved read-only analytical views. It must never be allowed to execute arbitrary write operations or access unrestricted operational tables.

## 5.4 MCP Integration

The platform should support the Model Context Protocol (MCP) to expose approved platform functionality to AI agents.

Potential MCP tools:

| Tool | Purpose |
|---|---|
| `get_sales_summary` | Retrieve sales metrics |
| `get_purchase_summary` | Retrieve purchase metrics |
| `get_inventory_status` | Retrieve inventory information |
| `get_customer_balance` | Retrieve customer balances |
| `get_supplier_balance` | Retrieve supplier balances |
| `get_top_selling_products` | Retrieve product rankings |
| `get_sales_trends` | Retrieve sales trends |
| `compare_business_periods` | Compare business performance |
| `get_report_metadata` | Retrieve report definitions |
| `search_platform_documentation` | Search platform documentation |

MCP should expose controlled capabilities rather than unrestricted database access.

MCP is an integration mechanism, not a requirement to expose every backend function as an AI tool. It should be implemented where it provides practical value.

## 5.5 AI Security Requirements

- AI agents must operate within the permissions of the requesting user.
- Business Analyst data access must be read-only.
- Agents must not modify operational records.
- Database queries must be validated and restricted.
- Query execution must have limits and timeouts.
- Sensitive data must not be sent to cloud providers without appropriate authorization.
- API keys must be securely stored.
- AI tool calls should be logged.
- AI-generated financial calculations must be traceable to approved metrics and source data.
- AI responses should identify the reporting period and relevant filters used.
- Unavailable or incomplete data must be disclosed rather than guessed.

---

# 6. Data Architecture

## 6.1 General Approach

The platform will maintain its own data environment, independent of the existing application.

The legacy database remains the source of truth for operational transactions.

The platform's data environment is used for:

- Data extraction.
- Data transformation.
- Business analytics.
- Reporting.
- Historical analysis.
- AI-powered insights.
- Platform-specific configuration and application data.

The platform must not become a second operational source of truth.

## 6.2 Development Strategy

Since the project currently does not have access to the company's actual database, development will start with a custom-designed synthetic data model.

This model will contain realistic pharmaceutical distribution data.

The architecture must allow the synthetic data source to be replaced or populated using real company data without requiring a complete redesign of the application.

Synthetic data should cover realistic business situations, including:

- Products and categories.
- Customers and suppliers.
- Sales and purchase invoices.
- Payments and receipts.
- Warehouses.
- Stock movements.
- Product batches and expiration dates.
- Returns and discounts.
- Different transaction statuses.
- Historical activity across multiple periods.

Synthetic data must be clearly identified as demonstration data and must never be confused with actual company records.

## 6.3 Proposed Data Layers

### Layer 1: Source and Staging

Stores extracted data from the legacy database with minimal transformation.

Responsibilities:

- Preserve source identifiers.
- Maintain source references.
- Track extraction timestamps.
- Support data validation.
- Facilitate debugging and reconciliation.

### Layer 2: Canonical Business Model

Contains standardized business entities and relationships.

This layer translates the legacy database structure into a consistent internal business model.

The canonical model should not depend on the naming conventions or internal structure of Mabna.

### Layer 3: Analytics and Semantic Layer

Contains:

- Approved business metrics.
- Aggregated datasets.
- Analytical views.
- Reporting calculations.
- KPI definitions.
- Business terminology and metric descriptions.

Both dashboards and AI agents should use this layer to ensure consistent results.

### Layer 4: Application Data

Stores platform-specific information such as:

- Users and roles.
- User preferences.
- AI provider configuration.
- Dashboard configuration.
- Audit logs.
- Platform documentation metadata.
- Synchronization status.

## 6.4 Proposed Database Entities

The following is an initial data model proposal, not a finalized schema.

| Domain | Entities |
|---|---|
| Products | Product, ProductCategory, Unit |
| Parties | Customer, Supplier |
| Sales | SalesInvoice, SalesInvoiceItem, SalesReturn |
| Purchases | PurchaseInvoice, PurchaseInvoiceItem, PurchaseReturn |
| Inventory | Warehouse, StockMovement, ProductBatch |
| Finance | Payment, Receipt, Account, JournalEntry |
| System | User, Role, AuditLog |
| Integration | SourceRecord, SyncJob, SyncLog |

Additional requirements:

- Support product batches and expiration dates.
- Support multiple units of measurement.
- Support returns and discounts.
- Support different invoice statuses.
- Preserve transaction dates and document references.
- Support historical records.
- Support Persian business terminology.
- Support Jalali calendar presentation.

The final schema must be based on validated business processes and the actual source data structure.

## 6.5 Financial Data Integrity

Financial metrics must have clearly defined calculation rules.

The platform must distinguish between:

- Sales revenue.
- Purchase costs.
- Gross profit.
- Net profit.
- Cash received.
- Cash paid.
- Customer receivables.
- Supplier payables.
- Inventory valuation.

The platform must not assume that sales revenue equals cash received or that purchases equal inventory costs.

Final accounting definitions must be validated against the company's actual accounting practices and the existing accounting system.

Every important financial KPI should have a documented definition, calculation method, and source.

---

# 7. Data Integration and Synchronization

## 7.1 Legacy System Integration

The expected legacy database technology is Microsoft SQL Server because the existing application operates in a Windows environment.

This is an assumption and must be verified once database access becomes available.

The integration process should:

1. Connect to the legacy SQL database using a dedicated read-only account.
2. Extract relevant records.
3. Store extracted data in staging.
4. Transform data into the canonical model.
5. Validate data integrity.
6. Update analytical datasets.
7. Record synchronization status and errors.

The integration must be implemented behind a backend service. Frontend clients must never connect directly to SQL Server.

## 7.2 Synchronization Requirements

The integration should eventually support:

- Initial full data extraction.
- Incremental synchronization.
- Scheduled synchronization.
- Error handling.
- Data reconciliation.
- Synchronization logs.
- Recovery from failed synchronization.
- Detection of updated or deleted source records where technically possible.

The exact synchronization frequency will be determined after examining the legacy database and company requirements.

## 7.3 Data Safety

The integration must be read-only.

The platform must never modify, delete, or create operational records in the legacy database.

The preferred production configuration is a dedicated read-only database account with access limited to the required tables or reporting views.

## 7.4 Data Reconciliation

Before analytical figures are used for management decisions, the platform must be able to compare selected results against the source system.

Examples:

- Sales totals for a selected period.
- Purchase totals.
- Customer balances.
- Supplier balances.
- Inventory quantities.

Discrepancies must be logged and investigated.

---

# 8. User Interface, Localization and Multi-Platform Support

## 8.1 Language

The primary interface language must be Persian.

English support may be added as a secondary language.

## 8.2 RTL Requirements

The interface must support:

- Right-to-left layouts.
- Persian typography.
- Persian labels and terminology.
- Persian date presentation.
- Jalali calendar support.
- Appropriate number and currency formatting.
- Persian search and filtering.

## 8.3 User Experience

The platform should prioritize simplicity.

The manager should be able to understand the company's general performance without requiring technical or accounting expertise.

Important metrics should be visually distinguishable.

Detailed reports should remain accessible without overwhelming the main dashboard.

The AI chat interface should be accessible from relevant parts of the platform.

## 8.4 Multi-Platform Architecture

The platform must support Windows desktop use in the MVP while preserving the ability to introduce mobile applications later.

A traditional Windows-only UI architecture must be avoided because it would require a separate frontend for mobile.

The preferred approach is to build one responsive web frontend and reuse it across supported platforms.

### Shared Frontend

**Technology:** React + TypeScript.

Responsibilities:

- Provide the main application interface.
- Support Persian RTL.
- Adapt to desktop, tablet, and mobile screen sizes.
- Communicate with the backend through documented APIs.
- Provide consistent behavior across platforms.

The frontend must not contain business-critical authorization rules or direct database access.

### Windows Desktop Application

**Technology:** Microsoft WebView2 with a lightweight Windows application host.

The Windows application will display the shared web frontend inside a desktop shell.

The desktop host may provide Windows-specific capabilities when needed, but business logic and primary UI functionality must remain in the shared frontend and backend.

The MVP will prioritize Windows desktop delivery.

### Future Mobile Support

Mobile application delivery is not part of the MVP, but the architecture must support it.

The planned approach is:

**First option: Progressive Web App (PWA)**

- Allow installation from supported mobile browsers.
- Reuse the existing responsive frontend.
- Avoid maintaining a separate mobile frontend.
- Provide a convenient home-screen experience.

**Future option: Capacitor**

If native mobile capabilities become necessary, the same web frontend can be packaged for Android and iOS using Capacitor.

This approach can provide access to native device capabilities without rebuilding the application from scratch.

### Mobile Scope

The future mobile experience should prioritize management use cases:

- Management KPI dashboard.
- Sales performance.
- Customer balances.
- Inventory alerts.
- Business insights.
- AI Business Analyst.
- Notifications, if required.

The mobile application does not necessarily need to reproduce every detailed desktop report or administrative screen.

### Mobile Security

- Mobile clients must communicate with the same backend API.
- Mobile clients must never connect directly to SQL Server.
- Authentication and authorization must be enforced by the backend.
- Remote access to the self-hosted platform must use secure HTTPS connections.
- Public access to the SQL Server database must not be permitted.
- Multi-factor authentication should be considered before enabling remote mobile access.
- Sensitive business data must not be stored in offline caches without an explicit security design.

### MVP Requirements for Future Compatibility

Although mobile delivery is deferred, the MVP must:

- Use a responsive frontend architecture.
- Keep the frontend independent from Windows-specific APIs.
- Expose application functionality through backend APIs.
- Use centralized authentication and authorization.
- Support touch-friendly controls where practical.
- Use adaptive layouts for dashboards and charts.
- Avoid assuming a fixed desktop screen size.
- Avoid storing sensitive business data in browser caches.

PWA installation, native mobile packaging, push notifications, biometric authentication, and offline functionality are outside the MVP unless separately approved.

---

# 9. Technical Architecture and Technology Stack

## 9.1 Recommended Technology Stack

The following is the recommended architecture for the project.

Individual technologies remain subject to technical validation and proof of concept.

| Component | Recommended technology | Purpose |
|---|---|---|
| Frontend | React + TypeScript | Shared responsive application |
| UI styling | Tailwind CSS or equivalent | UI development and RTL adaptation |
| Windows desktop host | WebView2 + .NET | Windows application shell |
| Backend | ASP.NET Core Web API | Application APIs and business logic |
| Platform database | PostgreSQL | Platform data, staging and analytics |
| Legacy database connector | SQL Server driver / ADO.NET or compatible library | Read-only Mabna integration |
| Data extraction | .NET background services | Scheduled extraction and synchronization |
| Data transformation | Backend services / SQL transformations | Canonical model and analytical datasets |
| BI platform | Metabase | Dashboards and visualization |
| AI analytics engine | Wren AI (candidate) | Natural-language analytics and semantic layer |
| AI orchestration | Custom backend agent service | Agent instructions, tools and permissions |
| AI protocol | MCP where useful | Controlled agent-tool integration |
| Local AI | Ollama-compatible endpoints | Local model inference |
| Cloud AI | API-based providers | External model inference |
| Deployment | Docker Compose | Self-hosted services |
| Future mobile packaging | PWA, optionally Capacitor | Mobile access |

## 9.2 Architecture Principles

The architecture should follow these principles:

1. One shared frontend for desktop and future mobile.
2. One backend API for all clients.
3. No direct database access from frontend clients.
4. Separation between operational source data and analytical data.
5. Separation between deterministic business calculations and AI-generated explanations.
6. Replaceable AI providers.
7. Modular services without unnecessary microservice complexity.
8. Self-hosted deployment.
9. Secure server-side authorization.
10. Independent integration adapters for the legacy system.

## 9.3 Platform Database Strategy

The platform should maintain its own data environment rather than depending directly on the Mabna schema.

PostgreSQL is the recommended initial platform database for the synthetic demo and the platform's internal data.

The existing SQL Server database remains the expected source system.

The integration layer will map source data into the platform's canonical model.

This separation allows the platform to continue functioning if the legacy system changes in the future.

The final decision on whether all staging and analytical data should remain in PostgreSQL or use SQL Server should be confirmed after the actual database and infrastructure are inspected.

## 9.4 BI Platform Evaluation

**Preferred candidate: Metabase.**

Metabase will be evaluated for:

- Dashboard creation.
- Filtering and drill-down.
- SQL database connectivity.
- Authentication integration.
- Embedding and integration options.
- Persian RTL compatibility.
- Mobile dashboard usability.
- Licensing and commercial deployment requirements.

Metabase is a candidate BI service, not an assumption that all dashboard requirements will automatically be satisfied.

If its embedding, localization, licensing, or customization limitations become significant, a custom React dashboard should be considered.

## 9.5 AI Analytics Engine Evaluation

**Preferred candidate: Wren AI.**

Wren AI will be evaluated for:

- Semantic modeling.
- Natural-language business questions.
- Text-to-SQL functionality.
- SQL Server and PostgreSQL compatibility.
- Local and cloud model integration.
- Integration with the custom backend.
- Permission and security boundaries.
- Licensing and feature availability.

Wren AI should not be allowed unrestricted access to the operational database.

If it does not satisfy the project's technical or licensing requirements, a custom controlled analytics service should be used instead.

## 9.6 AI Provider Abstraction

The backend must provide a common integration layer for AI providers.

The application should not depend on a single model provider.

Administrators should be able to configure:

- Provider type.
- Endpoint.
- API key.
- Model name.
- Relevant model parameters.
- Agent-specific model configuration.

The system should support local and cloud models through the same application-level interface where technically possible.

## 9.7 Deployment Architecture

The application will be self-hosted on company-controlled infrastructure.

Docker Compose is the preferred initial deployment approach.

The deployment may include:

- Backend API.
- Frontend application.
- Platform database.
- Data synchronization worker.
- BI service.
- AI analytics service.
- Local model service, if required.
- Reverse proxy and HTTPS configuration.

The Windows desktop application will connect to the deployed backend.

Future mobile clients will use the same backend API through secure access.

The deployment must be documented so that the system can be installed, updated, backed up, and restored by an administrator.

---

# 10. Security and Permissions

The platform must implement role-based access control.

Security requirements:

- User authentication.
- Role-based permissions.
- Protected administrative features.
- Secure database credentials.
- Secure AI provider credentials.
- Read-only legacy database integration.
- Audit logging.
- Restricted AI tool access.
- Protection against unauthorized data access.
- Backup and recovery procedures.
- Secure communication between clients and backend.
- HTTPS for remote access.
- Server-side validation of all sensitive operations.

Authorization must be enforced by the backend, not only by hiding or disabling frontend controls.

The platform should be designed for deployment within the company's controlled infrastructure.

---

# 11. Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-01 | The application must support self-hosted deployment. |
| NFR-02 | The application must support Docker-based deployment. |
| NFR-03 | The primary interface must support Persian RTL. |
| NFR-04 | The database architecture must be extensible and independent of the legacy schema. |
| NFR-05 | The application must support local and cloud AI providers. |
| NFR-06 | The system must protect confidential company data. |
| NFR-07 | Legacy database integration must be read-only. |
| NFR-08 | Financial calculations must be consistent across reports and AI responses. |
| NFR-09 | AI-generated insights must be traceable to supporting data. |
| NFR-10 | Synchronization failures must be logged. |
| NFR-11 | The application must support future analytical modules. |
| NFR-12 | The system must support user and role management. |
| NFR-13 | The frontend must be responsive and support future mobile clients. |
| NFR-14 | The backend API must be independent of the Windows desktop host. |
| NFR-15 | The system must prevent unauthorized direct database access from clients. |
| NFR-16 | Important data and synchronization processes must be auditable. |
| NFR-17 | The system must support backup and recovery procedures. |

---

# 12. MVP Scope

The MVP is intended to demonstrate the platform's analytical value using synthetic pharmaceutical distribution data.

It is not intended to replace Mabna or implement a complete ERP.

## 12.1 MVP Features

| Feature | Priority |
|---|---|
| Persian RTL interface | Must |
| Responsive frontend architecture | Must |
| Windows desktop application shell | Must |
| Synthetic pharmaceutical database | Must |
| Management overview dashboard | Must |
| Sales reports | Must |
| Purchase reports | Must |
| Inventory reports | Must |
| Customer and supplier balances | Must |
| Date filtering | Must |
| AI Business Analyst | Must |
| Local AI provider support | Must |
| API-based AI provider support | Must |
| User and role management | Must |
| Backend API | Must |
| Docker deployment | Must |
| Basic Platform Assistant | Should |
| Excel export | Should |
| PDF export | Should |
| Controlled MCP tools | Should |
| AI-generated business insights | Should |
| Data reconciliation examples | Should |
| Actual Mabna integration | Later phase |
| PWA installation | Future |
| Native Android/iOS application | Future |

## 12.2 MVP User Experience

The MVP should be desktop-first because the initial users will primarily use Windows computers.

However, the interface must be responsive from the beginning.

The MVP does not need to provide a complete mobile experience, but its architecture and frontend implementation must not prevent one.

## 12.3 Out of Scope

The initial MVP will not include:

- Replacing the existing accounting software.
- Replacing the existing data-entry workflow.
- Creating or editing operational transactions.
- Full ERP functionality.
- Automatic accounting decisions.
- Automatic financial transactions.
- Writing data into the legacy database.
- Advanced predictive forecasting without validated historical data.
- Automated business decisions without human approval.
- Native mobile applications.
- Mobile push notifications.
- Offline mobile analytics.
- Full mobile feature parity with desktop.

---

# 13. Development Roadmap

## Phase 1: Research and Architecture

Deliverables:

- Finalized technical architecture.
- Initial database schema.
- Data dictionary.
- Initial UI wireframes.
- Technology evaluation.
- Synthetic data requirements.
- Proof of concept for the shared frontend and Windows desktop shell.
- Initial responsive layout validation.

## Phase 2: Database and Demo Data

Deliverables:

- PostgreSQL platform database.
- Pharmaceutical distribution data model.
- Synthetic dataset.
- Data validation rules.
- Initial analytical views.
- Standardized KPI definitions.

## Phase 3: Dashboard and Reporting

Deliverables:

- Management dashboard.
- Sales reports.
- Purchase reports.
- Inventory reports.
- Customer and supplier reports.
- Filtering and data exploration.
- Export functionality where included in the MVP.

## Phase 4: AI Integration

Deliverables:

- AI provider abstraction.
- Local model integration.
- Cloud API integration.
- Platform Assistant.
- Business Analyst.
- Controlled analytics tools.
- MCP integration where useful.
- AI security and permissions.
- AI response traceability.

## Phase 5: Demo and Management Presentation

Deliverables:

- Working Windows demo.
- Sample business reports.
- AI analytical demonstrations.
- Deployment documentation.
- Presentation to management.
- Collection of feedback.
- Validation of management reporting requirements.

## Phase 6: Legacy Database Integration

This phase starts only after receiving the required authorization and database access.

Deliverables:

- Legacy database inspection.
- Database schema analysis.
- Data dictionary mapping.
- SQL Server connector.
- ETL implementation.
- Data reconciliation.
- Integration testing.
- Read-only access validation.

## Phase 7: Production Deployment

Deliverables:

- Production deployment.
- User configuration.
- Security validation.
- Backup configuration.
- Monitoring.
- Documentation.
- User training.

## Phase 8: Future Mobile Delivery

This phase is outside the MVP and will be considered after the desktop platform is validated.

Potential deliverables:

- Mobile PWA installation.
- Mobile dashboard optimization.
- Mobile AI assistant.
- Secure remote access.
- Push notifications if required.
- Capacitor-based Android/iOS packaging if native functionality is justified.
- Mobile-specific authentication and security validation.

---

# 14. MVP Acceptance Criteria

The initial demo will be considered successful when:

1. The application can run independently of the existing legacy software.
2. Synthetic pharmaceutical distribution data can populate the database.
3. The management dashboard displays meaningful business metrics.
4. Reports can be filtered by date and relevant business dimensions.
5. Sales, purchases, inventory, and financial information are presented consistently.
6. The manager can ask questions in Persian and receive data-supported answers.
7. The Platform Assistant can explain the application's functionality.
8. The AI provider can be configured to use either a local model or a cloud API.
9. AI agents can access approved analytical functionality through controlled tools.
10. The application can be deployed using the documented self-hosted deployment process.
11. The system does not require modification of the legacy application.
12. The architecture is ready for future SQL Server integration.
13. The Windows desktop application uses the shared frontend rather than a separate Windows-only UI.
14. The frontend adapts to smaller screen sizes.
15. The backend API is independent of the Windows desktop shell.

---

# 15. Risks and Open Questions

| Risk / Question | Impact | Proposed action |
|---|---|---|
| Actual database technology is unknown | Integration uncertainty | Verify during database inspection |
| Legacy schema is unknown | Data mapping uncertainty | Inspect schema and relationships |
| Long-term direction of Mabna is undecided | Future architecture uncertainty | Keep the integration layer independent |
| Management requirements are unclear | Dashboard scope uncertainty | Use demo for requirements discovery |
| Financial calculations may be complex | Reporting accuracy | Validate formulas with accounting staff |
| Historical data may be incomplete | Analytical accuracy | Perform data quality assessment |
| Metabase RTL support may be limited | UI limitations | Conduct technical prototype |
| Wren AI may not meet connector or security requirements | AI integration uncertainty | Conduct a focused proof of concept |
| AI may produce incorrect conclusions | Business risk | Use controlled tools and traceable calculations |
| Cloud AI may expose sensitive data | Privacy risk | Provide local model support and data controls |
| Local AI may require significant hardware | Infrastructure cost | Benchmark representative models |
| Windows WebView2 integration may introduce packaging issues | Desktop delivery risk | Validate the desktop shell early |
| PWA capabilities vary across mobile platforms | Future mobile limitations | Test supported browsers and consider Capacitor |
| Remote access may expose company data | Security risk | Require HTTPS, authentication and controlled network access |

## Questions to Resolve During Discovery

- What are the actual database tables and relationships?
- Which financial calculations does the company currently use?
- Are inventory batches and expiration dates recorded?
- Are sales returns and purchase returns tracked?
- How are customer and supplier balances calculated?
- Does the company have multiple warehouses?
- What is the expected data synchronization frequency?
- What hardware is available for self-hosting and local AI?
- What reports does management find most useful after seeing the initial demo?
- What level of remote access will eventually be required?
- Does the company have infrastructure suitable for secure mobile access?
- Which mobile use cases would provide the most value to management?

---

# 16. Success Metrics

The project should ultimately be evaluated based on business value rather than the number of features implemented.

Proposed success indicators:

- Reduction in time required to obtain business reports.
- Ability to access important business metrics from one interface.
- Successful reconciliation of analytical figures with the source system.
- Management adoption of the dashboard.
- Number of useful business questions answered through the platform.
- Reduction in manual reporting work.
- Operator ability to independently use the platform.
- Reliability of data synchronization.
- User satisfaction with the reporting experience.

Actual numerical targets should be established after observing the company's current reporting workflow.

---

# 17. Product Principles

The following principles should guide all implementation decisions.

1. **Do not replace what already works.** The existing software remains responsible for operational data entry until a separate decision is made.
2. **Data accuracy comes before visualization.** Incorrect numbers in a beautiful dashboard are worse than having no dashboard.
3. **Management requirements should evolve through discovery.** Do not assume we already know every report the manager needs.
4. **AI should complement traditional analytics.** Important business figures must come from deterministic calculations, not language-model guesses.
5. **Keep the architecture modular.** Dashboard, backend, database, and AI components should be replaceable independently.
6. **Protect company data.** The system must provide controlled access to sensitive business information.
7. **Build the demo independently.** Actual database access must not be a prerequisite for starting development.
8. **Keep the MVP focused.** Prioritize reporting, visibility, and decision support over unnecessary enterprise features.
9. **Build for multiple platforms from the beginning.** Use one shared frontend and backend rather than creating a Windows-only application that must later be rebuilt for mobile.
10. **Keep mobile delivery separate from MVP delivery.** Mobile compatibility is an architectural requirement, not an MVP feature commitment.

---

# 18. Final Product Vision

The long-term vision is to create a centralized intelligence platform for the pharmaceutical distribution company.

The platform will evolve beyond static reporting into a system that helps management understand:

- What is happening across the business.
- Why it is happening.
- Which areas require attention.
- What potential actions management could consider.

The existing software will initially continue handling operational processes, while the new platform will provide the analytical capabilities that are difficult to obtain from the existing workflow.

The platform will begin as a Windows-based management analytics application, but its shared web architecture will allow it to evolve into a multi-platform system accessible through desktop and mobile devices.

The ultimate goal is to transform the company's existing operational data into actionable business intelligence.

---

**Document version:** 1.1  
**Status:** Draft for MVP specialist review  
**Scope:** Initial demo and future integration architecture
