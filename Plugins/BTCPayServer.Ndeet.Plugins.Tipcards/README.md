# Tipcards

Tipcards is a store-level BTCPay Server plugin for creating printable Lightning tip cards. A store owner creates a set of cards, each card receives its own BTCPay pull payment, and the printed QR code opens a public claim page with an LNURL-withdraw flow.

The plugin is useful for handing out small amounts of sats at meetups, events, workshops, merchant onboarding sessions, or as simple paper vouchers.

## Requirements

- BTCPay Server `>= 2.3.4`.
- A store with permission to modify store settings.
- Lightning payouts (`BTC-LN`) enabled for the store before creating tipcard sets.

## Features

- Create named tipcard sets with a configurable number of cards.
- Set the amount per card in sats.
- Generate one pull payment per card.
- Use public claim pages that do not require a BTCPay account.
- Embed LNURL-withdraw data in the claim flow for compatible Lightning wallets.
- Show claimed, unclaimed, and total sats per set.
- View each card's claim page and QR code from the store admin UI.
- Customize the printed card headline, body text, and QR logo overlay.
- Print only unclaimed cards.
- Download printable PDFs for A4, A3, letter, or custom paper sizes.
- Configure PDF columns and optional cutting markers.
- Show optional Lightning wallet recommendations on the claim page.
- Display an approximate fiat value on the claim page when the store has a fiat default currency and rates are available.

## How It Works

1. Open the store where the cards should be funded from.
2. Go to the Tipcards entry under the store integrations navigation.
3. Create a new set with a name, sats-per-card amount, and card count.
4. The plugin creates pull payments for the cards and stores the pull payment IDs in store settings.
5. Print or download the unclaimed cards as a PDF.
6. Recipients scan a QR code, open the public claim page, and claim the sats with a Lightning wallet.

## Card Sets

Each set stores:

- Set name.
- Sats per card.
- Number of cards.
- Pull payment IDs for the generated cards.
- Creation date.
- Card headline.
- Card text.
- QR logo choice: Bitcoin, Lightning, or no overlay.

Card counts are limited to 500 cards per set. Amounts must be at least 1 sat.

## Editing Sets

Existing sets can be edited after creation.

- Changing text or QR logo settings updates the set metadata.
- Changing the sats amount recreates unclaimed cards with new pull payments.
- Increasing the card count creates additional pull payments.
- Decreasing the card count cancels unclaimed pull payments.
- The card count cannot be reduced below the number of already claimed cards.

## Claim Page

The public claim page shows:

- Store branding when configured.
- The sats amount.
- An optional fiat estimate.
- The card headline and text.
- A QR code and wallet launch link for LNURL-withdraw.
- An already-claimed state when a card has been redeemed.
- Optional wallet recommendations for users who do not have a Lightning wallet.

## Settings

Tipcards settings are stored per store.

The settings page currently controls wallet recommendations shown on claim pages. Recommendations are configured as a JSON array with `name`, `url`, and `description` fields.

Default recommendations include Blitz Wallet, Wallet of Satoshi, Aqua Wallet, and Phoenix.

## Limitations

- Tipcards require Lightning payouts to be configured before sets can be created.
- Printed and PDF views include only unclaimed cards.
- Deleting a set cancels the related pull payments and removes the set from plugin settings.
- This plugin is intended for small tip-card workflows, not high-volume voucher issuance.
