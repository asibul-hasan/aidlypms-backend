# Aidly PMS: Offline POS Plain Sales QA & Verification Guide

This document provides step-by-step instructions for testing and validating the Offline POS capability in Aidly PMS using browser developer tools and simulated network dropouts.

---

## Architecture Summary
- **Storage Layer**: IndexedDB database `AidlyPmsOfflineDb` with stores for `catalog_products`, `catalog_batches`, `catalog_customers`, `meta`, `sale_queue`, and `sync_log`.
- **Gateway & Connectivity**: `PosGatewayService` transparently routes POS lookups and sale creation between the live .NET 10 API and IndexedDB. `ConnectivityService` polls every 3 seconds with 2-failure hysteresis.
- **Auto-Sync Engine**: `SyncService` processes pending sales in FIFO order whenever the system transitions back online.
- **Idempotency & Replay Protection**: Each offline sale generates a client UUID `idempotency_key` and SHA256 payload hash to prevent double postings.
- **Clinical Safety Rules**: Narcotic and prescription-required products are strictly blocked from offline checkout.

---

## QA Test Scenarios

### Scenario 1: Initial Online Catalog Snapshot
1. Launch the application and log in as Cashier or Manager.
2. Navigate to the POS screen (`/customer/sale`).
3. Verify the **Header Status Chip** displays:
   - Green dot with **"Online"**.
   - Tooltip indicating catalog snapshot status (e.g., *"Catalog updated just now"*).
4. Verify IndexedDB (`DevTools` -> `Application` -> `Storage` -> `IndexedDB` -> `AidlyPmsOfflineDb`):
   - `catalog_products` contains active medicines.
   - `catalog_batches` contains non-expired batches with available quantity > 0.
   - `catalog_customers` contains up to 500 active customers.
   - `meta` contains `last_snapshot_at`, `snapshot_version`, and `sale_config`.

---

### Scenario 2: Network Drop Simulation & Offline Status
1. Open Chrome DevTools (`F12`).
2. Go to the **Network** tab.
3. In the throttling dropdown (default "No throttling"), select **"Offline"**.
4. Observe the Header Status Chip:
   - Within 3–6 seconds, the chip turns red and displays **"Offline"**.
   - Hovering over the chip displays offline guidance tooltip.

---

### Scenario 3: Clinical Safety Guard (Narcotic & Rx Block)
1. While still offline, search for a narcotic or prescription-required product (e.g. schedule POM or Narcotic).
2. Attempt to add it to the cart or complete checkout.
3. **Expected Result**: System rejects the addition with error message:
   `Offline sale of narcotic or prescription medicines is prohibited. Needs internet connection.`
4. The cart remains clean and clinical compliance is maintained.

---

### Scenario 4: Offline Sale Checkout & Thermal Receipt
1. Add an OTC medicine (e.g. Napa Extra 500mg, Paracetamol) to the cart.
2. Choose any supported payment method: Cash, Card, MFS (bKash/Nagad), Split, or Due.
3. Click **"Confirm Checkout"** (or press Enter / F2).
4. **Expected Result**:
   - The sale completes immediately with notification:
     `Offline Sale OFF-POS-1-YYYYMMDD-0001 completed and queued for sync!`
   - Local batch quantity in `catalog_batches` is decremented.
   - In thermal receipt mode, the receipt header prints:
     `OFFLINE - PENDING SYNC (OFF-POS-1-YYYYMMDD-0001)`
   - The Header Status Chip updates to show **"Offline (1)"**.

---

### Scenario 5: Offline Queue Drawer Management
1. Click the Header Status Chip.
2. The **Offline Sales Queue & Sync Management** modal opens.
3. Verify metrics:
   - **Pending Sync**: 1
   - **Sync Failed**: 0
   - **Synced**: 0
4. Inspect the table row:
   - Offline Receipt: `OFF-POS-1-YYYYMMDD-0001`
   - Client created timestamp
   - Total amount (৳)
   - Status badge: `Pending` (amber)
5. Click the **Eye icon** to inspect line items, batch, quantity, and payment breakdown.

---

### Scenario 6: Shift Close Guard Warning
1. While 1 or more offline sales remain in `Pending` status, navigate to Cash Counter (`/cash/receive`).
2. Click **"Cash Closer"**.
3. **Expected Result**:
   - A prominent amber warning banner appears:
     `Unsynced Offline Sales Warning: There are X offline sale(s) in the local queue. Closing the shift now will cause these sales to be booked with late_sync = TRUE at current server time once they are synced.`
   - Shift save is gated by confirmation checkbox:
     `[ ] I understand and confirm shift closure with unsynced offline sales`

---

### Scenario 7: Network Restoration & Automatic FIFO Sync
1. In DevTools Network tab, switch back to **"No throttling"** (Online).
2. Within 3 seconds, `ConnectivityService` detects network restoration:
   - The Header Status Chip turns blue with spinner: **"Syncing (1)"**.
   - `SyncService` POSTs the sale to `/api/v1/pos/sales` with `is_offline = true`, `idempotency_key`, and `client_created_at`.
   - On completion, the chip turns green: **"Online"**.
3. Open the Offline Sales Drawer:
   - Status transitions to `Synced` (green).
   - Server invoice number is displayed (e.g., `SAL-20261026-0042`).
   - If shift was closed prior to sync, the purple `Late Sync` tag appears.
   - Authoritative price variance is displayed if server master prices shifted.

---

### Scenario 8: Navigation & Unload Warning
1. Disconnect network again and create another offline sale.
2. Attempt to close or reload the browser tab.
3. **Expected Result**: Browser prompt appears:
   `You have unsynced offline sales. Leaving this page may delay synchronization.`

---

### Scenario 9: Offline Sales Review Screen
1. Navigate to `/customer/offline-review` (or from Sales menu -> "Offline Sales Review").
2. Verify all synced offline transactions with price variances or `late_sync = TRUE` are listed with:
   - Server Invoice # and Offline Receipt #
   - Client timestamp vs Server timestamp
   - Client Total vs Server Total
   - Calculated Price Variance
   - Shift Status (`Late Sync` vs `On-Time`)
