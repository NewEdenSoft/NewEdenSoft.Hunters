/** EVE's public image server (no authentication). https://developers.eveonline.com/docs/services/image-server/ */
export type EveImageSize = 32 | 64 | 128 | 256 | 512 | 1024

export function characterPortraitUrl(characterId: number, size: EveImageSize = 64): string {
  return `https://images.evetech.net/characters/${characterId}/portrait?size=${size}`
}
