import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';

export type InventoryAvailability = {
  sku: string;
  onHand: number;
  reserved: number;
  available: number;
  version: number;
};

export function getInventoryAvailability(sku: string): Promise<InventoryAvailability> {
  return apiRequest<InventoryAvailability>(
    `${serviceRoutes.inventory}/${encodeURIComponent(sku)}`,
  );
}
