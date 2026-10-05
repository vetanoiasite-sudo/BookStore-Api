# BookStore Marketplace — Implementation Plan

Source spec: `New Microsoft Word Document (3).md` (75 sections). Repository was empty at planning time (2026-09-08), so everything is built from scratch.

## 0. Environment findings

| Item | Found | Impact |
|---|---|---|
| .NET SDK | 10.0.400 | Matches spec (.NET 10) |
| Node / npm | 22.14 / 10.9 | OK |
| Angular CLI | 21.2.7 | Generates Angular 21 (spec says 19+) |
| SQL Server | Local default instance `MSSQLSERVER` running, plus LocalDB | Dev DB = local instance, `Server=.;Database=BookStore;Trusted_Connection=True;TrustServerCertificate=True` |
| Docker | Not installed | **Deferred by user decision. No Docker work in this plan.** |
| Angular install | npm 10.9 peer-resolution bug on the vitest graph | Solved with a committed lock file; fresh installs without it need `--legacy-peer-deps` once |
| dotnet-ef | 8.0.10 (global) | Run `dotnet tool update -g dotnet-ef` to 10.x before migrations |

## 1. Repository layout

```
BookStore/
├── BookStore-Api/                      (C:\projects\BookStore-Api)
│   ├── BookStore.slnx
│   ├── Directory.Build.props          (nullable, implicit usings, analyzers)
│   ├── Directory.Packages.props       (central package versions)
│   ├── src/
│   │   ├── BookStore.Domain/          entities, enums, state machines, domain exceptions
│   │   ├── BookStore.Application/     use cases (services), DTOs, validators, abstractions
│   │   ├── BookStore.Infrastructure/  EF Core, Identity, JWT, providers (payment/shipping/storage/ocr/search/notification), seed
│   │   └── BookStore.Api/             thin controllers, middleware, auth policies, Swagger, rate limiting
│   └── tests/
│       ├── BookStore.UnitTests/       xUnit + Shouldly + NSubstitute
│       └── BookStore.IntegrationTests/ WebApplicationFactory against local SQL Server test DB `BookStore_Test`
├── BookStore/                           (C:\projects\BookStore) Angular 21 standalone app
│   └── src/app/{core,shared,layout,features}
├── .env.example
├── README.md
└── PLAN.md
```

Dependency rule: Api → Infrastructure → Application → Domain. Domain has zero NuGet references. Application references only abstractions.

## 2. Key architecture decisions

1. **Identity**: ASP.NET Core Identity (`ApplicationUser : IdentityUser<Guid>`) for hashing, roles, lockout. Roles: `Admin`, `Staff`, `Seller`, `Buyer`. A user can hold Seller + Buyer. Admin/Staff are never combined with Seller/Buyer.
2. **JWT**: 15-minute access token with `sub`, `role`, `publicId`, `email_verified` claims; rotating refresh tokens stored hashed in `RefreshToken` table, revoked on logout or reuse.
3. **State machines** live in Domain as explicit transition tables:
   - `BookStateMachine`: Draft→PendingReview; PendingReview→Approved|Rejected; Rejected→Draft (edit and resubmit); Approved→WaitingForDelivery (auto on approve); WaitingForDelivery→Received; Received→Available (requires inventory location); Available→Reserved; Reserved→Available (payment failed/expired/cancelled); Reserved→Sold; Sold→Returned; Returned→Available|Archived; Draft|Approved|Available→Archived (seller withdraws). Anything else throws `InvalidStateTransitionException` → 409.
   - `OrderStateMachine`: PendingPayment→Paid|Cancelled; Paid→Processing; Processing→Packed; Packed→Shipped; Shipped→Delivered; Delivered→Completed|Returned; Returned→Refunded. Completed/Cancelled/Refunded are terminal.
   - Every book transition writes `BookStatusHistory` (from, to, actorId, reason, at).
4. **Unique-item inventory**: no `Quantity` on Book. Each physical copy is its own `Book` row. `OrderItem.Quantity` is always 1.
5. **Concurrency**: `Book.RowVersion` (SQL `rowversion`). Checkout runs in one DB transaction: load books → assert `Available` → set `Reserved` → create Order/OrderItems/Payment → `SaveChanges`. `DbUpdateConcurrencyException` → 409 "This book was just reserved by another buyer." Reservations expire after `Checkout:ReservationMinutes` (default 30) via a hosted service that cancels unpaid orders and releases books.
6. **Payment**: `IPaymentService` { CreatePaymentAsync, ConfirmPaymentAsync, RefundAsync }. `FakePaymentProvider` in MVP. `POST /api/payments/confirm` never trusts the client: it asks the provider for the intent status (the fake provider stores intents server-side and verifies an HMAC-signed token). Only a provider-confirmed result moves Order→Paid, Book→Sold, and posts wallet entries.
7. **Wallet = ledger**: `WalletTransaction` rows (Sale, Fee, Refund, Withdrawal, Adjustment) with `Status` (Pending/Available/Reversed) and `AvailableAt`. Balance is always computed from the ledger. On order Paid: `Sale +price` and `Fee −fee` as Pending. On order Completed: `AvailableAt = CompletedAt + Wallet:SettlementDays` (dev default 0). `WalletSettlementService` (hosted) flips Pending→Available; `POST /api/admin/wallets/settle` triggers it manually. Withdrawal request requires `Amount ≤ AvailableBalance`.
8. **Shipping**: `IShippingService` with `MockShippingProvider` generating tracking numbers. Admin: Paid→Processing→Packed→Shipped (creates `Shipment`). Buyer "confirm receipt": Shipped→Delivered→Completed in one use case (Delivered recorded in history). Auto-complete job after `Orders:AutoCompleteDays`.
9. **Privacy**: public APIs expose `Book.PublicId` (`BK-YYYY-NNNNNN` from a DB sequence) and `SellerPublicId` (`SL-xxxxxx`), never GUIDs, emails, or phones. `ContactInfoSanitizer` strips phones, emails, URLs, WhatsApp/Telegram handles from seller-authored text on save; FluentValidation also rejects obvious contact info. Buyer address is visible only to Admin/Staff. Seller sees items as "sold", never buyer identity. Support tickets are the only communication channel.
10. **Search**: `IBookSearchService` → `SqlBookSearchService` (EF projections, `AsNoTracking`, paged, sortable). `MostPopular` = weighted `ViewCount` + favorites count. Swappable for Elasticsearch/Qdrant later.
11. **Files**: `IFileStorageService` → `LocalFileStorageService` (`wwwroot/uploads/books/{bookPublicId}/{guid}.webp`). Validation: extension allowlist, 5 MB limit, magic-byte sniff, decode + resize with SkiaSharp (guarantees a real image). SkiaSharp replaces ImageSharp because ImageSharp 4.x requires a paid commercial licence.
12. **Audit**: `AuditLog` written by an EF `SaveChangesInterceptor` for attribute-marked entities, skipping `[SensitiveData]` properties; plus explicit business events.
13. **Notifications**: `INotificationService` → `InAppNotificationService`, called from use cases on BookApproved/BookRejected/BookSold/OrderPaid/OrderShipped/OrderDelivered/WithdrawalApproved.
14. **OCR-ready**: `IBookRecognitionService` → `MockBookRecognitionService`. Endpoint `POST /api/seller/books/recognize`.
15. **API envelope**: all responses `{ success, message, data, errors[] }`; global exception middleware maps Validation→400, Unauthorized→401, Forbidden→403, NotFound→404, Conflict/InvalidTransition→409, else 500. Serilog structured logging; secrets and PII excluded.
16. **No MediatR, no repositories**: Application services use `IAppDbContext` directly. Keeps to "no unnecessary abstractions".
17. **Configuration**: `appsettings.json` holds no secrets; values bound from env vars (`ConnectionStrings__Default`, `Jwt__Secret`, `Payment__FakeProviderKey`, `Storage__Root`). `.env.example` documents them. User-secrets in Development.
18. **Currency and fees**: `Platform:Currency` (default `EGP`), `Platform:FeePercent` (default 10), `Shipping:FlatCost` (default 30). All configurable.

## 3. Domain model (entities)

- Users and auth: `ApplicationUser` (+ `PublicId`, `DisplayName`, `IsSellerEnabled`), `RefreshToken`, `Address` (buyer addresses, soft-delete).
- Catalog: `Author`, `Publisher`, `Category` (NameAr/NameEn/Slug/ParentId/IsActive), `Book`, `BookCondition` (owned: cover/pages/writing/highlighting/torn/missing/yellowing/other + notes), `BookImage`, `BookStatusHistory`, `Favorite`.
- Inventory: `InventoryLocation` (Warehouse/Zone/Rack/Shelf/Box/Code), `InventoryItem` (BookId, LocationId, ReceivedAt, ReceivedBy, Notes).
- Commerce: `Cart`, `CartItem`, `Order`, `OrderItem` (title/author/ISBN/condition/image snapshot), `Payment`, `Shipment`, `Review` (5 sub-scores + comment, unique per OrderItem).
- Finance: `Wallet`, `WalletTransaction`, `Withdrawal`.
- Platform: `SupportTicket`, `SupportMessage`, `Notification`, `AuditLog`, `PlatformSetting`.

Indexes per spec §45 plus `Favorite(UserId, BookId)` unique, `Review(OrderItemId)` unique, `RefreshToken(TokenHash)`, `Book(SellerId, Status)`, `WalletTransaction(WalletId, Status)`.

## 4. REST API surface

- `/api/auth`: register, login, refresh, logout, me, change-password, forgot-password, reset-password, verify-email, resend-verification
- `/api/books` (public): list/search, `{publicId}`, `{publicId}/similar`
- `/api/categories`, `/api/authors`, `/api/publishers` (public read)
- `/api/cart`, `/api/cart/items`, `/api/favorites`
- `/api/orders` (buyer): create (checkout), list, `{orderNumber}`, `{orderNumber}/confirm-receipt`, `{orderNumber}/cancel`
- `/api/payments`: create, confirm, `webhook/{provider}`
- `/api/addresses`, `/api/reviews`, `/api/notifications`, `/api/support/tickets`
- `/api/seller`: dashboard, books (CRUD + images + submit + archive), sales, wallet, wallet/transactions, withdrawals, books/recognize
- `/api/admin`: dashboard, books (pending, approve, reject, receive, assign-location, status), inventory (locations, search by publicId/ISBN/title, history), orders (process, pack, ship), users, sellers, payments, wallets (+settle), withdrawals (approve/reject/mark-paid), shipments, reviews, support, categories, reports, audit-logs, settings

Authorization policies: `RequireAdmin`, `RequireStaff` (Admin|Staff), `RequireSeller`, `RequireBuyer`, `RequireVerifiedEmail` (checkout and selling; relaxed in Development).

## 5. Frontend plan (Angular 21, standalone, signals)

- `core/`: `ApiClient` (base URL from `environment`), `AuthService` (signals), interceptors (bearer, refresh-on-401, error→toast), guards (`authGuard`, `sellerGuard`, `adminGuard`, `guestGuard`), `I18nService` (loads `assets/i18n/{ar,en}.json`, `t` pipe, sets `<html lang dir>`), `ToastService`, `DialogService`, `SeoService`, domain services (`BookService`, `CartService`, `OrderService`, `SellerService`, `AdminService`, `NotificationService`, `FavoriteService`, `SupportService`).
- `shared/`: `ui-button`, `ui-card`, `ui-skeleton`, `ui-empty-state`, `ui-error-state`, `ui-dialog`, `ui-pagination`, `ui-badge`, `ui-form-field`, `ui-image-uploader`, `book-card`, `price` pipe, `condition-label` pipe, `data-table`, `stat-card`, `mini-chart` (inline SVG).
- `layout/`: `public-shell` (header, search, mobile drawer, footer), `dashboard-shell` (responsive sidebar), `auth-shell`.
- `features/`: `home`, `books` (list + filters), `book-details` (`/books/:slug-:publicId`), `categories`, `how-it-works`, `auth`, `cart`, `checkout` (address → payment → result), `buyer/` (orders, order-details, favorites, addresses, notifications, profile), `seller/` (dashboard, my-books, book-form, sales, wallet, withdrawals, notifications), `admin/` (dashboard, books, book-review, inventory, orders, users, sellers, payments, wallets, withdrawals, shipping, reviews, support, categories, reports, audit-logs, settings), `support`.
- Design tokens (SCSS): bg `#FAF9F6`, surface `#FFFFFF`, text `#1C1B18`, primary `#1F4D3A` (dark green), accent `#7A5C3E` (brown), muted `#6B6760`, border `#E6E2DA`. Arabic font stack: "IBM Plex Sans Arabic", "Noto Naskh Arabic", system. Grid: 2/3/4-5 columns.
- Every data page has Loading (skeleton) / Empty / Error / Success states.

## 6. Phases, deliverables and verification

Each phase ends with `dotnet build` (+ `dotnet test` from phase 2) and `ng build` (from phase 5) passing.

| # | Phase | Deliverable | Verify |
|---|---|---|---|
| 1 | Structure | **Done.** Solution, 4 projects + 2 test projects, Angular app scaffolded, `Directory.*.props`, Serilog, Swagger, CORS, rate limiting, envelope + status-code middleware, design tokens, `.env.example` | Build, 28 tests, API and dev proxy verified |
| 2 | Domain | **Done.** 61 domain files: entities with behaviour, 15 enums, 6 state machines, transition table, public id and slug helpers | Build clean, 452 tests pass |
| 3 | EF Core + SQL Server | **Done.** `AppDbContext`, 30 entity configurations, 76 indexes, RowVersion, two sequences, audit interceptor, retryable transactions, initial migration, seed, auto-migrate in Development | Migration applied to a fresh database, 42 integration tests pass |
| 4 | Auth | **Done.** Identity, JWT with rotating hashed refresh tokens and replay detection, ten auth endpoints, five authorization policies, validation filter, configurable rate limits, logging mail channel | 60 auth tests over HTTP, 15 token unit tests |
| 5 | Books | **Done.** Public list/details, `SqlBookSearchService`, view counting, image upload pipeline with re-encoding, contact sanitizer. Frontend: storefront shell, home, books grid, book page, Arabic and English translations, design tokens | 26 catalogue tests, 25 front-end tests, both builds clean |
| 6 | Categories | **Done.** Public tree with rolled-up counts, breadcrumb endpoint, admin tree and CRUD with cycle and deletion guards, categories page, category filter on the catalogue | 29 category tests, 8 front-end tests |
| 7 | Seller workflow | **Done.** Seller book CRUD (draft and rejected only), image upload, submit, resubmit after reject, withdraw; admin approve/reject/receive/assign-location with the inventory row and the sale opened in one transaction; status timeline; in-app notifications; mock OCR endpoint; `InvalidUploadException` mapped to 400. Frontend: sign-in and sign-up, session restore, bearer and refresh interceptor, four route guards, dashboard shell, seller dashboard/my-books/book-form, admin queue and review screen, 192 new translation keys | 22 seller-workflow tests over HTTP including the full seller→admin→available journey, 22 validator unit tests, 33 front-end tests, both builds clean |
| 8 | Search/filter | **Done.** `GET /api/books/filters` counting every facet with its own filter excluded, so a choice stays changeable; author, publisher, language, multi-select condition, price and year range on the listing; `FacetDimension` exclusion in `SqlBookSearchService`. Frontend: `BookFilters` panel with counts, range boxes applied on request, mobile drawer, filtered empty state, 18 translation keys, the query string still the only source of truth | 21 catalogue-filter tests over HTTP, 11 new front-end tests, 708 backend and 77 front-end tests pass, both builds clean |
| 9 | Cart and favorites | **Done.** Basket and saved list for a signed-in buyer: add, remove, empty, and a saved list that is idempotent to save and unsave. Every basket answer is read against a live catalogue, so a copy sold or repriced while the basket sat open comes back flagged rather than silently changed; a listing not on public sale answers 404 rather than 409. One `BookSummary` projection now describes a card for the catalogue, the basket and the saved list alike. Frontend: basket page with per-line notices and an order summary, saved-books page, hearts on every card, working buy and save buttons on the book page, header counts as signals, 39 translation keys | 26 cart and favourites tests over HTTP, 734 backend tests pass, both builds clean |
| 10 | Orders/checkout | **Done.** Saved addresses with one default that checkout pre-selects; checkout as a single transaction that re-reads the copies, reserves them, writes the order with its snapshots and empties the basket, answering 409 `book_just_reserved` when another buyer won the race; buyer cancel that releases the copies; confirm receipt; a hosted service that cancels lapsed reservations and puts the copies back on sale. Frontend: checkout page, addresses page, orders list and order page with its progress trail, shared address form, 91 translation keys | Both builds clean. Tests deferred at the user's request; the concurrency test moves to phase 16 |
| 11 | Payments | `IPaymentService`, fake provider, create/confirm/webhook, Paid transition, Book→Sold, refund path | tests |
| 12 | Wallet | Ledger, fee calc, settlement job, withdrawals (seller request, admin approve/reject/paid), seller wallet UI, admin wallet/withdrawal UI | unit tests: ledger math, payout rules |
| 13 | Shipping | `IShippingService`, mock provider, admin process/pack/ship, buyer tracking view, auto-complete job | tests |
| 14 | Admin dashboard | **Done, minus what phases 11-13 have to bring first.** Dashboard built around the queues, with two inline-SVG charts over a fortnight; all orders, with a pick list showing which shelf each copy is on; accounts, where closing one ends its sessions; sellers, with verify, suspend and reinstate; the warehouse - one search box over book code, shelf code, ISBN and title, plus move-between-shelves with a reason, movement history and shelf management; the audit trail and the settings, both administrator-only; and a report over any window. **Not built:** payments, wallets, withdrawals and shipping screens, which have no data until phases 11-13; reviews, which need a completed order; and support, which has no buyer-side channel yet | Ran the API and checked every new endpoint by hand: all answer 200 with sensible data, staff are refused the trail and the settings, a buyer is refused the whole area, and the move-and-history path was exercised end to end. No automated tests, at the user request |
| 15 | Frontend polish | Empty/error states, dialogs, toasts, skeletons, SEO tags, canonical, mobile nav, accessibility pass, EN translations complete | `ng build --configuration production` |
| 16 | Tests | Fill gaps: cart, checkout, wallet, payout, concurrency, state machines; integration for auth/books/orders/admin | `dotnet test` green |
| 17 | Docs | README per spec §59; acceptance-scenario script (`scripts/acceptance.ps1`) | Run acceptance scenario end to end |

Docker (spec §48) is deferred. Local run: `dotnet run --project src/BookStore.Api` in `C:\projects\BookStore-Api` and `npm start` in `C:\projects\BookStore`.

## 7. Acceptance scenario (spec §73) mapping

Steps 1–5 → phases 4, 5, 7. Steps 6–9 → phase 7. Steps 10–17 → phases 8–11. Steps 18–23 → phase 13. Steps 24–28 → phase 12. Privacy is checked in every phase: DTO review, sanitizer tests, and an integration test asserting no email/phone fields appear in public, seller, or buyer responses.

## 8. Development accounts (seed, Development only)

| Role | Email | Password |
|---|---|---|
| Admin | admin@bookstore.local | Dev@12345! |
| Staff | staff@bookstore.local | Dev@12345! |
| Seller | seller@bookstore.local | Dev@12345! |
| Buyer | buyer@bookstore.local | Dev@12345! |

Seed runs only when `ASPNETCORE_ENVIRONMENT=Development` and the DB has no users.

## 9. Assumptions and open points

- Currency default `EGP`, platform fee 10%, flat shipping, settlement days 0 in dev (7 suggested for prod). All configurable.
- Email verification is architected but not enforced in Development.
- Angular 21 is used (CLI installed); satisfies "Angular 19+".
- Docker deferred by user; local SQL Server is the dev database. `docker-compose.yml` can be added later without code changes.
- QR code generation is out of MVP; `PublicId` and an admin scan/enter-code input are provided so a scanner can be dropped in.
- No admin impersonation (spec §67).
- Reviewing books and checking parcels in is daily work rather than an administrator's privilege, so the back-office guard is `staffGuard` (Admin or Staff) rather than the `adminGuard` named in §5. Reshaping the category tree stays administrator-only.
- Two transitions raised by one action (approve then await-delivery; return-to-draft then resubmit) are stamped a tick apart. The timeline is sorted by time, and two rows written at the same instant have no defined order.
- Destructive seller actions confirm through the browser dialog until `ui-dialog` arrives in phase 15.
- Phase 7 added `GET /api/admin/inventory/locations` because shelving a copy needs the lookup. The rest of the inventory screen stays in phase 14.
- Phase 9 made `ApiResponse<T>.Data` always written, including when null. The envelope omitted it while it was null, which is not open to a payload that is a value type: the first boolean endpoint threw rather than answering, and `false` would have been indistinguishable from "no data" anyway.
- The basket page's checkout button is disabled until phase 10 delivers checkout, and says so rather than pretending to be busy.
- Saving a book works on a copy that has already been sold: it is a bookmark, not a purchase. Adding one to a basket does not.
- Phase 10 was built without tests at the user's request. The concurrency check the phase called for — parallel checkouts on one copy, exactly one succeeding — is listed in phase 16 instead, and nothing else in phase 10 is covered.
- `RequireVerifiedEmail` now passes for any signed-in caller in Development. There is no mail server there, so enforcing it would leave every account created on a developer machine unable to buy anything. Outside Development the claim is still required, and checkout is the first endpoint behind it.
- A buyer can only cancel an order nobody has paid for. Cancelling a paid one means moving money back, and refunds arrive with payments in phase 11.
- The basket now reports `shippingCost` and `total` as well as the subtotal, so the figure shown before checkout is the figure the server writes the order with rather than one the page worked out for itself.
- Phases 11 (payments), 12 (wallet) and 13 (shipping) are postponed at the user's request, and phase 14 was built before them. Everything in the back office that depends on money or on a parcel moving is therefore missing rather than half-built: no payments, wallets or withdrawals screen, no fulfilment actions on an order, and no reviews, because a review needs a completed order and nothing can complete yet.
- Support tickets are still unbuilt on both sides. The spec makes them the only channel between a buyer and a seller, so this is a gap rather than a deferral, and it needs a phase of its own.
- The settings screen is read-only. The values come from configuration, and a form that wrote them to the settings table would either not take effect or quietly disagree with the environment the API is running in.
- Reading the back office is staff work; closing an account, judging a seller, and reading the audit trail or the settings are administrator work. The two policies are applied per action rather than per controller.
- Two report aggregates had to be written as plainer queries than they read: a filtered aggregate inside a `GroupBy`, and a grouping keyed through two joins. The second only surfaced by running the endpoint, which is what running it was for.
