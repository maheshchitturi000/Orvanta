import { IconName } from '../../shared/ui/icon/icon';

export type IntegrationCategory = 'food' | 'jobs' | 'travel';

/** What the UI knows about an integration regardless of the user's connection. */
export interface CatalogEntry {
  /** Provider / source key used by the API and in the URL (/integrations/food/swiggy). */
  id: string;
  category: IntegrationCategory;
  name: string;
  /** Letter badge(s) shown as the logo… */
  logo: string[];
  /** …or an icon, for entries without a brand mark. */
  icon?: IconName;
  tagline: string;
  features: string[];
  /** Shown when the server reports the integration as unavailable. */
  unavailableNote?: string;
  /** Not available yet: listed, but can't be opened. */
  comingSoon?: boolean;
}

export interface CategoryInfo {
  key: IntegrationCategory;
  label: string;
  icon: IconName;
}

export const CATEGORIES: CategoryInfo[] = [
  { key: 'food', label: 'Food & delivery', icon: 'food' },
  { key: 'jobs', label: 'Jobs', icon: 'briefcase' },
  { key: 'travel', label: 'Travel', icon: 'plane' }
];

export const CATALOG: CatalogEntry[] = [
  {
    id: 'swiggy',
    category: 'food',
    name: 'Swiggy',
    logo: ['S'],
    tagline: 'Search dishes and place cash-on-delivery orders.',
    features: [
      'Search restaurants and dishes from the AI Assistant',
      'Compare prices, ratings and delivery times with Zomato',
      'Cash-on-delivery orders, always confirmed by you first',
      'Delivers to the default address you choose here'
    ]
  },
  {
    id: 'zomato',
    category: 'food',
    name: 'Zomato',
    logo: ['Z'],
    tagline: 'Search Zomato restaurants alongside Swiggy for better top picks.',
    unavailableNote: 'Zomato only accepts apps it has approved. Orvanta is waiting for approval.',
    features: [
      'Restaurants and dishes from Zomato in every food search',
      'Ranked together with Swiggy results',
      'Cash-on-delivery orders, always confirmed by you first',
      'Delivers to the default address you choose here'
    ]
  },
  {
    id: 'ondc',
    category: 'food',
    name: 'ONDC',
    logo: ['O'],
    tagline: "Order from restaurants on India's open food network.",
    unavailableNote: 'ONDC is turned off for this workspace.',
    features: [
      'Restaurants from every seller app on ONDC, in one search',
      "The restaurant's own price, with delivery, packing and taxes",
      'Cash-on-delivery orders, always confirmed by you first',
      'No account to connect'
    ]
  },
  {
    id: 'jsearch',
    category: 'jobs',
    name: 'LinkedIn · Indeed · Glassdoor',
    logo: ['in', 'i', 'G'],
    tagline: 'Jobs from LinkedIn, Indeed, Glassdoor and Foundit that match your CV.',
    features: [
      'Listings from LinkedIn, Indeed, Glassdoor, Foundit and company sites',
      'Any country, built from your target roles × locations',
      'Every match scored against your CV',
      'Free plan: about 200 searches a month'
    ]
  },
  {
    id: 'adzuna',
    category: 'jobs',
    name: 'Adzuna',
    logo: ['A'],
    tagline: 'Jobs from Indian job boards and company sites, through Adzuna’s official API.',
    features: [
      'Listings Adzuna gathers from job boards and company sites in India and 18 other countries',
      'Searched by your target roles and cities',
      'Every match scored against your CV',
      'Free developer key'
    ]
  },
  {
    id: 'flights',
    category: 'travel',
    name: 'Flights',
    logo: [],
    icon: 'plane',
    tagline: 'Search and compare flights from chat.',
    features: [],
    comingSoon: true
  },
  {
    id: 'trains',
    category: 'travel',
    name: 'Trains',
    logo: [],
    icon: 'train',
    tagline: 'Check availability and plan train journeys.',
    features: [],
    comingSoon: true
  },
  {
    id: 'hotels',
    category: 'travel',
    name: 'Hotels',
    logo: [],
    icon: 'bed',
    tagline: 'Find stays that fit your trip and budget.',
    features: [],
    comingSoon: true
  }
];

export function categoryInfo(key: IntegrationCategory): CategoryInfo {
  return CATEGORIES.find(c => c.key === key)!;
}

export function detailLink(entry: CatalogEntry): string[] | null {
  return entry.comingSoon ? null : ['/integrations', entry.category, entry.id];
}
