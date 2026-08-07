using Application;
using Application.Contracts;
using Application.DTOs;
using Application.DTOs.AccountCategory;
using Application.DTOs.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[Authorize]
public class AccountCategoriesController : BaseController
{
    private readonly ICategoryService<AccountCategoryDto, UpsertAccountCategoryDto> _accountCategoryService;
    private readonly Application.Services.AccountCategoryService _accountCategoryServiceConcrete;

    public AccountCategoriesController(
        ICategoryService<AccountCategoryDto, UpsertAccountCategoryDto> accountCategoryService,
        Application.Services.AccountCategoryService accountCategoryServiceConcrete)
    {
        _accountCategoryService = accountCategoryService;
        _accountCategoryServiceConcrete = accountCategoryServiceConcrete;
    }

    [HttpPost("search")]
    public async Task<IActionResult> GetPaged([FromBody] QueryParameters queryParams)
    {
        var result = await _accountCategoryService.GetPagedAsync(UserId!, queryParams);
        return HandleResult(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _accountCategoryService.GetAllAsync(UserId!);
        return HandleResult(result);
    }

    [HttpPost("upsert")]
    public async Task<IActionResult> Upsert([FromBody] UpsertAccountCategoryDto dto)
    {
        var result = await _accountCategoryService.UpsertAsync(UserId!, dto);
        return HandleResult(result);
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteDto dto)
    {
        var result = await _accountCategoryService.DeleteAsync(UserId!, dto.Id);
        return HandleResult(result);
    }

    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] Application.DTOs.AccountCategory.MergeAccountCategoriesDto dto)
    {
        var result = await _accountCategoryServiceConcrete.MergeAsync(UserId!, dto);
        return HandleResult(result);
    }
}