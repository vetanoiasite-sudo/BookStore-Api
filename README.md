# BookStore — Used Book Marketplace

A marketplace for used books in which **the platform is the only intermediary**.
Sellers list books, the platform reviews, stores, sells, ships and settles. Buyers
and sellers never exchange contact details and cannot reach each other directly.

```
Seller  →  Platform  →  Buyer
```

The platform is two independent projects, each in its own folder:

| Project | Path | Stack |
|---|---|---|
| Backend API | `C:\projects\BookStore-Api` (this folder) | ASP.NET Core Web API on .NET 10, EF Core, SQL Server |
| Frontend | `C:\projects\BookStore` | Angular 21, standalone components, signals, SCSS, RTL-first |

They are built, tested and deployed separately, and talk to each other only over HTTP.

## Status

Phases 1 to 10 and 14 of 17 are complete. Payments, the wallet and shipping (11 to 13) are postponed.

Phase 1 built the project structure, layering, the shared response envelope, error
handling, logging, Swagger, CORS, rate limiting, the design token system and the
test harness.

Phase 2 built the domain model: entities that drive their own lifecycles, fifteen
enumerations, and six state machines covering books, orders, payments, shipments,
payouts and support tickets.

Phase 3 built persistence: the database context, thirty entity configurations, the
audit trail, the concurrency token that stops one copy being sold twice, the initial
migration and the development seed.

Phase 4 built authentication: registration, sign-in, rotating refresh tokens with
replay detection, sign-out, password change and reset, and email confirmation, behind
five named authorization policies.

Phase 5 built the public catalogue and the first real screens: search, book pages,
the image upload pipeline, the contact-detail sanitizer, and an Arabic-first
storefront that switches to English from one control.

Phase 6 built categories: the public tree with rolled-up book counts, breadcrumbs,
administrative management with guards against cycles and unsafe deletion, and the
category pages.

Phase 7 built the workflow the whole platform turns on: a seller writes a listing and
photographs the copy, the platform reviews it, the warehouse receives it and puts it
on a shelf, and only then does it appear in the catalogue. It comes with the seller
workspace, the back-office review screen, sign-in and sign-up, and in-app
notifications telling a seller what was decided.

Phase 8 built search and filtering: a filter panel counted against the results the
reader has already narrowed to, with each facet counted with its own filter left out
so a choice stays changeable.

Phase 9 built the basket and the saved list. Both belong to an account, both hold
nothing, and both are read against a live catalogue, so a copy sold or repriced while
a basket sat open is reported rather than quietly changed.

Phase 10 built checkout and orders. A basket becomes an order in one transaction that
re-reads the copies, reserves them and empties the basket, so two buyers reaching the
same copy together end with exactly one order and one clear refusal. A hosted service
puts the copies of an abandoned checkout back on sale.

Phase 14 built the back office: a dashboard arranged around the queues, every order
with a pick list saying which shelf each copy is on, accounts and sellers, the
warehouse, the audit trail, a report over any window, and the settings the platform
runs on. What it does not have is everything that waits on money or on a parcel
moving - payments, wallets, withdrawals, fulfilment and reviews - because phases 11
to 13 are postponed. See `PLAN.md` for the full phase list.

## Requirements

- .NET SDK 10.0
- Node.js 22 and npm 10
- SQL Server (a local default instance is enough for development)

## Backend

```bash
cd C:\projects\BookStore-Api
dotnet restore
dotnet build
dotnet test
dotnet run --project src/BookStore.Api
```

The API listens on `https://tempopen.bsite.net`. In Development, Swagger UI is served
at `https://tempopen.bsite.net/swagger` and a health check at `/api/health`.

### Architecture

```
src/
  BookStore.Domain/          entities, enums, state machines, domain exceptions
  BookStore.Application/     use cases, DTOs, validators, provider abstractions
  BookStore.Infrastructure/  EF Core, identity, JWT, payment/shipping/storage providers
  BookStore.Api/             thin controllers, middleware, auth policies
tests/
  BookStore.UnitTests/       state machines, pricing, wallet, cart rules
  BookStore.IntegrationTests/ HTTP-level tests against the real host
```

Dependencies point inward only: `Api → Infrastructure → Application → Domain`.
`BookStore.Domain` deliberately has no package references, and a test enforces it.

### Database

```bash
cd C:\projects\BookStore-Api
dotnet tool install -g dotnet-ef      # once, version 10.x
dotnet ef database update       --project src/BookStore.Infrastructure --startup-project src/BookStore.Api
dotnet ef migrations add <Name> --project src/BookStore.Infrastructure --startup-project src/BookStore.Api --output-dir Persistence/Migrations
```

In Development the API migrates and seeds itself on startup, so these commands are
only needed when working on the schema. Seeding is idempotent and adds only what is
missing. Both steps are configurable: `Database:AutoMigrate` and `Seed:Enabled`.

### Development accounts

Seeded in Development only. All four share the same password, `Dev@12345!`.

| Role | Email |
|---|---|
| Admin | admin@bookstore.local |
| Staff | staff@bookstore.local |
| Seller and Buyer | seller@bookstore.local |
| Buyer | buyer@bookstore.local |

The seed also creates nine categories, six authors, three publishers, six warehouse
shelves and eight books: six on sale and two waiting in the review queue.

### Authentication

Sign-in returns a short-lived access token and a refresh token. The access token
carries only an identifier, a display name, the roles and whether the address is
confirmed. It deliberately carries no email address or phone number, because a JWT
payload is readable by anyone holding it.

Refresh tokens are stored as a hash and replace themselves on every use. Presenting
one that has already been used is treated as theft and ends every session on that
account. Changing or resetting a password does the same.

| Endpoint | Purpose |
|---|---|
| `POST /api/auth/register` | Create an account, optionally with a seller profile |
| `POST /api/auth/login` | Sign in |
| `POST /api/auth/refresh` | Exchange a refresh token for a new pair |
| `POST /api/auth/logout` | End this session, or every session |
| `GET /api/auth/me` | Describe the signed-in caller |
| `POST /api/auth/change-password` | Change a known password |
| `POST /api/auth/forgot-password` | Request a reset link |
| `POST /api/auth/reset-password` | Complete a reset |
| `POST /api/auth/verify-email` | Confirm an address |
| `POST /api/auth/resend-verification` | Send a fresh confirmation link |

Sign-in, password reset and confirmation never reveal whether an address has an
account: a wrong password and an unknown address return exactly the same response.

Authorization uses five named policies rather than role strings scattered across
controllers: admin, staff, seller, buyer, and confirmed address.

In Development, mail is written to the log instead of being sent. The link appears at
debug level, so a confirmation or reset can be followed from the console output. For
the same reason the confirmed-address policy passes for any signed-in caller there:
with no mail server, enforcing it would leave every account created on a developer
machine unable to buy anything. Outside Development the claim is required, and
checkout is the first endpoint behind it.

### Catalogue

The catalogue is public and needs no sign-in. Only copies that are on sale appear; a
draft, a listing still in review, or a sold copy returns 404 rather than an explicit
refusal, so the endpoint does not confirm what exists.

| Endpoint | Purpose |
|---|---|
| `GET /api/books` | Search, filter, sort and page the catalogue |
| `GET /api/books/filters` | The values worth offering in the filter panel |
| `GET /api/books/{publicId}` | One book, by code or by full URL segment |
| `GET /api/books/{publicId}/similar` | Other copies worth showing beside it |
| `GET /api/books/new-arrivals` | Newest copies, for the home page |
| `GET /api/books/featured` | Most viewed copies, for the home page |

Books are addressed by a public code such as `BK-2026-000001`. A book URL pairs a
slug with that code, so a link with an outdated slug still resolves and the canonical
tag points at the current address.

Results are always paged. A caller asking for a very large page gets a clamped one.

A listing can be narrowed by search term, category (which includes everything beneath
it), author, publisher, language, one or more condition grades, a price range and a
range of publication years, and ordered by newest, oldest, price either way, or how
often a copy has been looked at. Repeat `condition` to accept several grades.

`GET /api/books/filters` takes the same query string and answers with the values
worth offering beside those results, each with the number of copies behind it. Two
rules make the panel usable: a value that would return nothing is never offered, and
each facet is counted with its own filter left out, so a choice can still be changed
or a price range widened once it has been made.

### Categories

Categories form a tree and are addressed by slug, so an internal identifier never
appears on a public endpoint. Each node reports the copies on sale in it and in
everything beneath it, so a parent never looks emptier than its children. The
storefront counts only books actually on sale; the administrative view counts every
book whatever its status.

| Endpoint | Purpose |
|---|---|
| `GET /api/categories` | The tree of active categories |
| `GET /api/categories/{slug}` | One category with its breadcrumb and children |
| `GET /api/admin/categories` | The whole tree, including hidden categories |
| `POST /api/admin/categories` | Create a category |
| `PUT /api/admin/categories/{id}` | Rename, reorder, show or hide |
| `POST /api/admin/categories/{id}/move` | Move under a different parent |
| `DELETE /api/admin/categories/{id}` | Delete a category nothing points at |

Staff can read the tree; only an administrator can reshape it, because a move or a
deletion affects every book filed underneath.

Three rules keep the tree a tree. A category cannot be placed inside itself or
beneath one of its own descendants. A category holding books cannot be deleted, and
the refusal says how many are in the way. A category with children cannot be deleted
either. Hiding a category is the safe alternative: it disappears from the storefront
and keeps everything filed under it.

A rename never changes the slug, because that is what existing links and search
results point at.

### Selling a book

A used copy is a physical object, so listing one is not a form submission but a
sequence of things that actually happen. Each step is its own call, and the state
machine refuses any other order.

| Step | Who | Endpoint | Result |
|---|---|---|---|
| Write the listing | Seller | `POST /api/seller/books` | Draft, visible to nobody |
| Photograph the copy | Seller | `POST /api/seller/books/{code}/images` | A cover, which review requires |
| Send for review | Seller | `POST /api/seller/books/{code}/submit` | In review |
| Approve | Staff | `POST /api/admin/books/{code}/approve` | Awaiting delivery |
| Reject | Staff | `POST /api/admin/books/{code}/reject` | Back to the seller, with a reason |
| Receive the parcel | Staff | `POST /api/admin/books/{code}/receive` | Held by the warehouse |
| Shelve it | Staff | `POST /api/admin/books/{code}/assign-location` | On sale, and findable |

Approval is not availability. A copy the platform has said yes to is a parcel it is
expecting, and it stays out of the catalogue until it has arrived and been given a
shelf. Shelving and going on sale happen in one transaction, because a copy listed as
available but never shelved cannot be found when a buyer pays for it.

A rejection carries a reason, is stored on the listing and is sent to the seller. The
seller edits and resubmits the same listing rather than starting again, and the reason
is cleared when they do.

The seller stops being able to edit a listing the moment it goes to review: after that
the platform is describing a copy it has inspected, and the description cannot be
swapped for a different book. A draft the platform has never seen can be deleted;
anything past that is withdrawn instead, so the record of what was decided survives.

`POST /api/seller/books/recognize` reads a photograph of a cover and suggests title,
author, publisher, ISBN and year. It saves nothing and reports its own confidence: the
seller confirms or corrects every field. A mock provider ships with the MVP, behind
`IBookRecognitionService`.

Sellers are told what was decided through in-app notifications, written by
`INotificationService`. Delivery never fails a request: the book really was approved,
and reporting otherwise would be worse than an undelivered message.

### The basket and the saved list

Both belong to a signed-in buyer, and neither holds anything: a copy in a basket is
still on sale to everybody else until somebody pays for it at checkout.

| Endpoint | Purpose |
|---|---|
| `GET /api/cart` | The basket, priced as the catalogue stands now |
| `POST /api/cart/items` | Add one copy |
| `DELETE /api/cart/items/{publicId}` | Remove one copy |
| `DELETE /api/cart` | Empty the basket |
| `GET /api/favorites` | One page of saved books, most recent first |
| `GET /api/favorites/codes` | Every saved code, so a grid can fill in its hearts |
| `GET /api/favorites/{publicId}` | Whether one book is saved |
| `POST /api/favorites` | Save a book |
| `DELETE /api/favorites/{publicId}` | Unsave a book |

Because every listing is a single physical copy, a basket has no quantities: a line
either exists or it does not. Three additions are refused with 409 and a code saying
which rule was broken — `book_not_available` for a copy that is no longer on sale,
`book_already_in_cart` for the same copy twice, and `cannot_buy_own_book` for a
seller's own listing. A book that is not on public sale answers 404 rather than 409,
so the basket cannot be used to discover what is sitting in the review queue.

Every basket call answers with the whole basket rather than with the line that
changed, because the answer is read against a live catalogue: adding one copy can be
the moment another one in it turns out to have been sold. The response says
`hasUnavailableItems` and `hasPriceChanges`, each line carries the price it was added
at beside what it costs now, and the subtotal counts only what can still be bought.

Saving and unsaving are both idempotent: a second tap on a heart agrees with the
first rather than failing.

### Checkout and orders

An order is the only link between a buyer and a seller, and each side sees its own
view of it. Nothing a buyer reads names the seller; nothing a seller reads names the
buyer or says where a parcel went.

| Endpoint | Purpose |
|---|---|
| `GET /api/addresses` | Saved delivery addresses, the default first |
| `POST /api/addresses` | Save an address; the first one becomes the default |
| `PUT /api/addresses/{id}` | Edit one |
| `POST /api/addresses/{id}/default` | Make it the one checkout pre-selects |
| `DELETE /api/addresses/{id}` | Hide it from the list |
| `POST /api/orders` | Place the order for everything in the basket |
| `GET /api/orders` | The buyer's own orders, newest first |
| `GET /api/orders/{orderNumber}` | One order |
| `POST /api/orders/{orderNumber}/cancel` | Call off an unpaid order |
| `POST /api/orders/{orderNumber}/confirm-receipt` | The parcel arrived |

Checkout is the one place a copy stops being available to everybody, and every
listing is a single physical book, so two buyers arriving together is the ordinary
way a popular copy is bought rather than an edge case. The whole of it is one
transaction: the copies are re-read inside it rather than trusted from the basket,
reserved, written into the order with their snapshots, and the basket is emptied. A
buyer who loses the race gets 409 with `book_just_reserved` and reloads their basket;
there is nothing to retry, because there was only ever one copy. The concurrency
token on the book row is what decides it.

The order keeps its own copy of everything: the title, author, ISBN, condition and
cover of each book, and the delivery address as it stood. Editing a listing or an
address afterwards cannot rewrite what an old order says.

Reserved copies are held for `Checkout:ReservationMinutes` (30 by default). A hosted
service sweeps up the checkouts nobody paid for, cancels them and puts the copies
back on sale — without it, one abandoned basket would take a book off the market for
good. It can be tuned or switched off with `Checkout:ExpirySweepSeconds` and
`Checkout:ExpirySweepEnabled`.

A buyer may cancel an order nobody has paid for, which releases the copies straight
away, and may confirm a parcel arrived, which records the delivery and closes the
order. Cancelling a paid order means moving money back, so it waits for refunds.

Addresses are never deleted, only hidden: an order carries a copy of one, and that
copy has to stay traceable back to where it came from.

### The back office

Reading the back office is staff work. Judging a person is not, so closing an account,
verifying or suspending a seller, and reading the audit trail or the settings are
administrator-only.

| Endpoint | Purpose |
|---|---|
| `GET /api/admin/dashboard` | The queues, the totals and two fortnight charts |
| `GET /api/admin/orders` | Every order, filterable by status |
| `GET /api/admin/orders/{orderNumber}` | One order, with a pick list and the buyer |
| `GET /api/admin/users` | Accounts, searchable and filterable by role |
| `POST /api/admin/users/{id}/active` | Open or close an account (administrator) |
| `GET /api/admin/sellers` | Sellers, with what each has listed and sold |
| `POST /api/admin/sellers/{id}/verify` | Mark a seller as checked (administrator) |
| `POST /api/admin/sellers/{id}/suspend` | Stop them listing, with a reason (administrator) |
| `POST /api/admin/sellers/{id}/reinstate` | Let them list again (administrator) |
| `GET /api/admin/inventory/items` | Find a copy by book code, shelf code, ISBN or title |
| `POST /api/admin/inventory/items/{id}/move` | Move a copy to another shelf |
| `GET /api/admin/inventory/items/{id}/history` | Everywhere that copy has been |
| `GET/POST/PUT /api/admin/inventory/locations` | The shelves themselves |
| `GET /api/admin/audit-logs` | Who did what, and when (administrator) |
| `GET /api/admin/reports` | What happened over a window of time |
| `GET /api/admin/settings` | What the platform is running on (administrator) |

The dashboard opens with the queues rather than with the takings: a listing nobody
reviewed and an order nobody packed are the two ways this marketplace actually fails,
so each queue is a number that links straight to the work.

The order page is the only view in the platform that sees both sides of a sale. It
carries the buyer and the delivery address, which no seller ever sees, and the shelf
each copy sits on, which is the part only the warehouse can answer.

The warehouse screen is one search box rather than four fields, because the person
using it is standing at a shelf holding something - a book with a code on it, a shelf
with a code on it, an ISBN, or just a title - and one box takes all four. Moving a
copy records who moved it and why.

The audit trail says who did what to which row. It deliberately does not carry the
values that changed: those can hold personal details, and a trail that leaks them is
worse than no trail.

The settings screen is read-only, and says so. These values come from the environment
the API runs in; a form that wrote them somewhere else would either not take effect or
quietly disagree with it.

Closing an account stops it signing in and ends the sessions it already has. It
removes nothing: the orders, listings and history stay, because the platform has to be
able to answer for them afterwards. Suspending a seller stops new listings and nothing
else - the copies already on sale stay on sale, because a buyer with one in a basket
has done nothing wrong.

### Privacy

A book page says the seller is verified and how many sales they have completed. It
carries no name, no email address, no phone number and no internal identifier. A test
asserts that no catalogue response contains any of these.

Seller-written text is passed through a sanitizer that removes email addresses, phone
numbers in Arabic or Latin digits, web addresses, social handles and mentions of
messaging apps. It is a filter rather than a guarantee, and is documented as such.

### Uploads

Images are decoded and re-encoded rather than written through. A file that does not
decode is not an image, whatever its name or declared type claims, and a re-encoded
file cannot carry a payload past an extension check. The client's file name is never
used. Uploads are capped while streaming rather than after reading them into memory.

### Response envelope

Every endpoint returns the same shape, success or failure:

```json
{ "success": true,  "message": null, "data": { } }
{ "success": false, "message": "…", "errors": [ { "code": "…", "message": "…", "field": "…" } ] }
```

`data` is omitted when it is null, and always written otherwise — including for a
payload that is a value type, where "no data" and `false` would look the same on the
wire if it were left out.

Status codes: 400 validation, 401 unauthenticated, 403 forbidden, 404 not found,
409 conflict or invalid state transition, 429 rate limited, 500 server error.
Framework-generated responses are filled in with the same envelope.

## Frontend

```bash
cd C:\projects\BookStore
npm install
npm start          # http://localhost:4200
npm run build
npm test
```

`npm start` proxies `/api` and `/uploads` to the API on `http://localhost:5080`, so the two
projects can run side by side without CORS configuration during development.

### Structure

```
src/
  environments/     apiBaseUrl and other runtime settings; never hard-coded in components
  styles/           design tokens and mixins shared by every component stylesheet
  app/core/         api client, config, guards, interceptors, domain services
  app/shared/       reusable presentational components, pipes, directives
  app/layout/       public shell, dashboard shell, auth shell
  app/features/     one folder per feature, all lazy-loaded
```

The UI is Arabic-first and right-to-left. Layout uses CSS logical properties, so
switching the `dir` attribute flips the whole interface.

No user-visible string is written inside a component. Every one is a key looked up in
`src/assets/i18n/ar.json` or `en.json`, which is what makes the interface switchable
and makes a missing translation visible rather than silent.

## Configuration

No secrets live in source control. `appsettings.json` ships empty values, and
`.env.example` documents every variable. Environment variables override
configuration files; a double underscore separates section from key, for example
`Jwt__Secret`.

For local development, `appsettings.Development.json` carries obviously fake
values and points at `Server=.;Database=BookStore`.

## Notes

- Docker is intentionally not part of the current setup. Development runs against
  the local SQL Server instance.
- npm 10.9 hits a peer-resolution bug on the Angular test dependency graph. The
  committed `package-lock.json` avoids it; if you install from scratch without the
  lock file, use `npm install --legacy-peer-deps` once.
