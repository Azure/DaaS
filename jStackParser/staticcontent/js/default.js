/*global $, console*/
/*
  By Mostafa Omar
	https://www.facebook.com/MostafaOmarIbrahiem
*/
$(function () {
    'use strict';
    (function () {
        var aside = $('.side-nav'),
            showAsideBtn = $('.show-side-btn'),
            contents = $('#contents');

        showAsideBtn.on("click", function () {
            $("#" + $(this).data('show')).toggleClass('show-side-nav');
            contents.toggleClass('margin');
        });

        if ($(window).width() <= 767) {
            aside.addClass('show-side-nav');
        }
        $(window).on('resize', function () {
            if ($(window).width() > 767) {
                aside.removeClass('show-side-nav');
            }
        });

        // dropdown menu in the side nav
        var slideNavDropdown = $('.side-nav-dropdown');
        $('.side-nav .categories li').on('click', function () {
            $(this).toggleClass('opend').siblings().removeClass('opend');
            if ($(this).hasClass('opend')) {
                $(this).find('.side-nav-dropdown').slideToggle('fast');
                $(this).siblings().find('.side-nav-dropdown').slideUp('fast');
            } else {
                $(this).find('.side-nav-dropdown').slideUp('fast');
            }
        });
        $('.side-nav .close-aside').on('click', function () {
            $('#' + $(this).data('close')).addClass('show-side-nav');
            contents.removeClass('margin');
        });
    }());
});