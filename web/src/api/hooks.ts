import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import * as api from './client';
import type { CreateBoxRequest, DepositRequest, MoveRequest, WithdrawRequest } from './types';

export const queryKeys = {
  saves: ['saves'] as const,
  saveBoxes: (saveId: string) => ['saves', saveId, 'boxes'] as const,
  vaultBoxes: ['vault', 'boxes'] as const,
  vaultPokemon: ['vault', 'pokemon'] as const,
  vaultPokemonDetail: (id: string) => ['vault', 'pokemon', id] as const,
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

/** Every vault mutation reshapes both the vault and the open save. */
function useInvalidatingMutation<TReq>(fn: (req: TReq) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.vaultBoxes });
      void queryClient.invalidateQueries({ queryKey: queryKeys.vaultPokemon });
      void queryClient.invalidateQueries({ queryKey: queryKeys.saves });
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
