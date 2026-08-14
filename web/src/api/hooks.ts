import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as api from './client';
import type {
  BulkDepositRequest,
  BulkMoveRequest,
  CreateBoxRequest,
  DepositRequest,
  ItemDepositRequest,
  ItemWithdrawRequest,
  MoveRequest,
  ReleaseRequest,
  TradeRequest,
  WithdrawRequest,
} from './types';

export const queryKeys = {
  saves: ['saves'] as const,
  saveBoxes: (saveId: string) => ['saves', saveId, 'boxes'] as const,
  vaultBoxes: ['vault', 'boxes'] as const,
  vaultPokemon: ['vault', 'pokemon'] as const,
  vaultPokemonDetail: (id: string) => ['vault', 'pokemon', id] as const,
  vaultPokemonLegality: (id: string) => ['vault', 'pokemon', id, 'legality'] as const,
  nationalDex: ['dex', 'national'] as const,
  saveDex: (saveId: string) => ['dex', 'saves', saveId] as const,
  items: ['items'] as const,
  config: ['config'] as const,
};

export function useSaves() {
  return useQuery({ queryKey: queryKeys.saves, queryFn: api.listSaves });
}

export function useSaveBoxes(saveId: string | null) {
  return useQuery({
    queryKey: queryKeys.saveBoxes(saveId ?? 'none'),
    queryFn: () => api.getSaveBoxes(saveId as string),
    enabled: saveId !== null,
  });
}

export function useVaultBoxes() {
  return useQuery({ queryKey: queryKeys.vaultBoxes, queryFn: api.listVaultBoxes });
}

export function useVaultPokemon() {
  return useQuery({ queryKey: queryKeys.vaultPokemon, queryFn: api.listVaultPokemon });
}

export function useVaultPokemonDetail(id: string | null) {
  return useQuery({
    queryKey: queryKeys.vaultPokemonDetail(id ?? 'none'),
    queryFn: () => api.getVaultPokemon(id as string),
    enabled: id !== null,
  });
}

export function useVaultLegality(id: string | null) {
  return useQuery({
    queryKey: queryKeys.vaultPokemonLegality(id ?? 'none'),
    queryFn: () => api.getVaultLegality(id as string),
    enabled: id !== null,
  });
}

export function useNationalDex() {
  return useQuery({ queryKey: queryKeys.nationalDex, queryFn: api.getNationalDex });
}

export function useSaveDex(saveId: string | null) {
  return useQuery({
    queryKey: queryKeys.saveDex(saveId ?? 'none'),
    queryFn: () => api.getSaveDex(saveId as string),
    enabled: saveId !== null,
  });
}

export function useItems() {
  return useQuery({ queryKey: queryKeys.items, queryFn: api.listItems });
}

export function useConfig() {
  return useQuery({ queryKey: queryKeys.config, queryFn: api.getConfig, staleTime: Infinity });
}

/** Every vault mutation reshapes the vault, the open save, the item vault, and both dexes. */
function useInvalidatingMutation<TReq, TRes>(fn: (req: TReq) => Promise<TRes>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.vaultBoxes });
      void queryClient.invalidateQueries({ queryKey: queryKeys.vaultPokemon });
      void queryClient.invalidateQueries({ queryKey: queryKeys.saves });
      void queryClient.invalidateQueries({ queryKey: queryKeys.nationalDex });
      void queryClient.invalidateQueries({ queryKey: ['dex', 'saves'] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.items });
    },
  });
}

export function useUploadSave() {
  return useInvalidatingMutation((file: File) => api.uploadSave(file));
}

export function useCreateVaultBox() {
  return useInvalidatingMutation((req?: CreateBoxRequest) => api.createVaultBox(req));
}

export function useDeposit() {
  return useInvalidatingMutation((req: DepositRequest) => api.deposit(req));
}

export function useWithdraw() {
  return useInvalidatingMutation((req: WithdrawRequest) => api.withdraw(req));
}

export function useMove() {
  return useInvalidatingMutation((req: MoveRequest) => api.move(req));
}

export function useDepositMany() {
  return useInvalidatingMutation((req: BulkDepositRequest) => api.depositMany(req));
}

export function useMoveMany() {
  return useInvalidatingMutation((req: BulkMoveRequest) => api.moveMany(req));
}

export function useRelease() {
  return useInvalidatingMutation((req: ReleaseRequest) => api.release(req));
}

export function useTrade() {
  return useInvalidatingMutation((req: TradeRequest) => api.trade(req));
}

export function useDepositItem() {
  return useInvalidatingMutation((req: ItemDepositRequest) => api.depositItem(req));
}

export function useWithdrawItem() {
  return useInvalidatingMutation((req: ItemWithdrawRequest) => api.withdrawItem(req));
}
