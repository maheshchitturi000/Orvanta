export interface FoodItem {
  provider: string;
  providerName: string;
  menuItemId: string;
  name: string;
  restaurantId: string;
  restaurantName: string;
  price: number | null;
  rating: number | null;
  ratingCount: number | null;
  isVeg: boolean | null;
  isBestseller: boolean;
  inStock: boolean;
  requiresCustomization: boolean;
  imageUrl: string | null;
  /** "variants" | "variantsV2" when the dish has size/portion options. */
  variantFormat: string | null;
  /** Restaurant's default option per variant group; used when ordering. */
  defaultVariants: { groupId: string; variationId: string }[] | null;
  /** e.g. "Half · Serves 1" */
  variantLabel: string | null;
}

export interface RankedFoodItem {
  rank: number;
  score: number;
  item: FoodItem;
}

export interface ProviderIssue {
  provider: string;
  message: string;
  needsConnection: boolean;
}

export interface FoodAssistResult {
  isFoodIntent: boolean;
  query: string;
  vegOnly: boolean;
  autoPickTop: boolean;
  items: RankedFoodItem[];
  providersSearched: string[];
  issues: ProviderIssue[];
}

export interface CartLine {
  name: string;
  quantity: number;
  total: number;
}

export interface CartPreview {
  provider: string;
  providerName: string;
  restaurantName: string;
  items: CartLine[];
  itemTotal: number;
  deliveryFee: number;
  taxes: number;
  discount: number;
  toPay: number;
  paymentMethod: string;
  addressId: string;
  cartId: string | null;
}

export interface OrderPreview {
  previewId: string;
  expiresAtUtc: string;
  itemName: string;
  cart: CartPreview;
}

export interface OrderConfirmation {
  orderId: string;
  provider: string;
  providerOrderId: string | null;
  status: string;
  restaurantName: string;
  itemName: string;
  quantity: number;
  totalAmount: number;
  paymentMethod: string;
  message: string;
}
